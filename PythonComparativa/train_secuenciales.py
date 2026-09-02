"""
train_secuenciales.py - GRU y FNN sliding window para el juego actual
====================================================================
Entrena las dos arquitecturas que aprovechan el tiempo (la GRU, que arrastra
un estado oculto, y la ventana deslizante, que ve el fotograma anterior junto
al actual) con el MISMO protocolo que train_clasificacion_fnn.py, para que sus
cifras se puedan poner al lado de las del FNN sin asteriscos:

  - mismas 40 variables y mismo filtro de spawn,
  - misma correccion de timing y misma ventana de intencion,
  - mismo split temporal 80/20 (nada de aleatorio: a 10 Hz dos fotogramas
    seguidos son casi el mismo estado),
  - sin SMOTE, con pos_weight y perdida de accion enmascarada por puertas,
  - mismas 4 acciones, incluidas las teclas K y L.

Por que ahora si tiene sentido entrenarlas
------------------------------------------
Estas dos arquitecturas suponen que la distancia temporal entre fotogramas en
ejecucion es la misma que en el CSV. Hasta que los controladores pasaron a
inferir a 10 Hz eso no se cumplia: el juego decidia a ~60 Hz, asi que el
"fotograma anterior" de la ventana estaba a 16 ms en vez de a 100, y el estado
oculto de la GRU avanzaba seis veces mas rapido que durante el entrenamiento.
Con inferenceInterval = 0.1 la condicion se cumple y la comparacion es limpia.

Diferencias deliberadas respecto al script original de cada una
--------------------------------------------------------------
1. La GRU se entrena con la perdida en TODOS los pasos de la secuencia, no
   solo en el ultimo. En el juego el estado oculto rueda sin parar, asi que el
   modelo tiene que acertar en cualquier punto del recorrido, no solo despues
   de exactamente diez pasos.
2. Las variables se recortan a +/-3 desviaciones, que es lo que hacen los
   controladores en Unity. Antes se entrenaba sin recorte y se inferia con el,
   asi que los valores extremos llegaban al modelo distintos de como los
   habia visto.

Uso:
    python train_secuenciales.py --csv SoccerData_2026_09_02_01_09_17.csv \
                                 --nombre 10minutos --salida ../Assets
"""
import argparse
import json
import os
import sys

import numpy as np
import pandas as pd
import torch
import torch.nn as nn
import torch.optim as optim
from sklearn.metrics import (accuracy_score, precision_score, recall_score,
                             f1_score, classification_report)

# El protocolo (filtro de spawn, correccion de timing, ventana de intencion y
# split temporal) se reutiliza tal cual del script del FNN en vez de copiarlo:
# si se copiara, cualquier cambio alli dejaria de aplicarse aqui y la
# comparativa se volveria mentira sin avisar.
for _c in ['../PythonTrainingLegacy', '../PythonTraining', '.']:
    _r = os.path.join(os.path.dirname(os.path.abspath(__file__)), _c)
    if os.path.exists(os.path.join(_r, 'train_clasificacion_fnn.py')):
        sys.path.insert(0, _r)
        break
else:
    raise SystemExit('No encuentro train_clasificacion_fnn.py. Ponlo en '
                     'PythonTrainingLegacy/ o al lado de este script.')

import train_clasificacion_fnn as F

EPOCAS = 150
BATCH = 32
LR = 0.001
PESO_MOV = 1.0
PESO_ACC = 2.0
RECORTE = 3.0            # las mismas 3 sigmas que aplica NormalizeFeatures en Unity

SEQ_GRU = 50             # pasos por trozo de BPTT (5 segundos de juego)
PASO_GRU = 10            # solape entre trozos
BATCH_GRU = 16           # trozos por lote (cada uno aporta 50 pasos de perdida)
OCULTO = 64

ACCIONES = ['Disparo', 'Pase', 'RoboK', 'RoboL']
N_ACC, N_MOV = 4, 9


# ============================================================ arquitecturas
class CeldaGRU(nn.Module):
    """GRU escrita a mano (puertas de reset y update) para que el grafo ONNX
    sea plano y el Inference Engine de Unity lo cargue sin operadores raros."""
    def __init__(self, entrada, oculto):
        super().__init__()
        self.oculto = oculto
        self.W_ir = nn.Linear(entrada, oculto)
        self.W_hr = nn.Linear(oculto, oculto, bias=False)
        self.W_iz = nn.Linear(entrada, oculto)
        self.W_hz = nn.Linear(oculto, oculto, bias=False)
        self.W_in = nn.Linear(entrada, oculto)
        self.W_hn = nn.Linear(oculto, oculto, bias=False)

    def forward(self, x, h):
        r = torch.sigmoid(self.W_ir(x) + self.W_hr(h))
        z = torch.sigmoid(self.W_iz(x) + self.W_hz(h))
        n = torch.tanh(self.W_in(x) + r * self.W_hn(h))
        return (1 - z) * n + z * h


class SoccerGRU(nn.Module):
    def __init__(self, entrada=40, oculto=OCULTO):
        super().__init__()
        self.oculto = oculto
        self.celda = CeldaGRU(entrada, oculto)
        self.rama_mov = nn.Sequential(nn.Linear(oculto, 32), nn.ReLU(), nn.Linear(32, N_MOV))
        self.rama_acc = nn.Sequential(nn.Linear(oculto, 32), nn.ReLU(), nn.Linear(32, N_ACC))

    def forward(self, x, h=None):
        """x: [B, T, 40]. Devuelve logits en TODOS los pasos: [B, T, 9] y [B, T, 4]."""
        if h is None:
            h = torch.zeros(x.size(0), self.oculto, device=x.device)
        ms, as_ = [], []
        for t in range(x.size(1)):
            h = self.celda(x[:, t, :], h)
            ms.append(self.rama_mov(h))
            as_.append(self.rama_acc(h))
        return torch.stack(ms, 1), torch.stack(as_, 1), h


class GRUPaso(nn.Module):
    """Un solo paso, que es como lo ejecuta AIControllerGRU: recibe el estado
    oculto anterior y devuelve el nuevo. Softmax y sigmoide van dentro porque
    el controlador compara las salidas directamente contra actionThreshold."""
    def __init__(self, base):
        super().__init__()
        self.base = base

    def forward(self, x, h_in):                       # x [1,1,40], h_in [1,1,64]
        x2 = x.reshape(-1, x.shape[-1])
        h2 = h_in.reshape(-1, self.base.oculto)
        h = self.base.celda(x2, h2)
        mov = torch.softmax(self.base.rama_mov(h), dim=-1)
        acc = torch.sigmoid(self.base.rama_acc(h))
        return mov, acc, h.reshape(1, 1, self.base.oculto)


class FNNVentana(nn.Module):
    """Sin recurrencia: concatena el fotograma anterior y el actual (80 valores)."""
    def __init__(self, entrada=80):
        super().__init__()
        self.rama_mov = nn.Sequential(
            nn.Linear(entrada, 128), nn.ReLU(),
            nn.Linear(128, 64), nn.ReLU(), nn.Linear(64, N_MOV))
        self.rama_acc = nn.Sequential(
            nn.Linear(entrada, 128), nn.ReLU(),
            nn.Linear(128, 64), nn.ReLU(), nn.Linear(64, N_ACC))

    def forward(self, x):
        return self.rama_mov(x), self.rama_acc(x)


class VentanaExport(nn.Module):
    def __init__(self, base):
        super().__init__()
        self.base = base

    def forward(self, x):
        m, a = self.base(x)
        return torch.softmax(m, dim=-1), torch.sigmoid(a)


# ==================================================================== datos
def clase_movimiento(df):
    dx = np.where(df['InputX'] > 0.5, 1.0, np.where(df['InputX'] < -0.5, -1.0, 0.0))
    dz = np.where(df['InputZ'] > 0.5, 1.0, np.where(df['InputZ'] < -0.5, -1.0, 0.0))
    return ((dx + 1) * 3 + (dz + 1)).astype(np.int64)


def puertas(df):
    """En que acciones tiene sentido preguntar en cada fila. Identico al FNN."""
    tiene = df['TienePelota'].values > 0.5
    rival = (~tiene) & (df['TienePelotaEquipo'].values > 1.5)
    return np.stack([
        tiene.astype(np.float32),
        tiene.astype(np.float32),
        (rival & (df['DistPelota'].values <= F.RANGO_SPRINT)).astype(np.float32),
        (rival & (df['DistPelota'].values < F.RANGO_ROBO)).astype(np.float32),
    ], 1)


def tensores(df, mean=None, std=None):
    """Igual que SoccerDataset pero SIN barajar: el orden temporal es el dato."""
    Xr = df[FEATURES].values.astype(np.float32)
    if mean is None:
        mean = Xr.mean(0)
        std = Xr.std(0)
        std[std == 0] = 1.0
    X = np.clip((Xr - mean) / std, -RECORTE, RECORTE).astype(np.float32)
    return (X, Xr, clase_movimiento(df), df[ACCIONES].values.astype(np.float32),
            puertas(df), mean, std)


# Las 40 variables, en el mismo orden que RecopilarVariablesDelEntorno() en C#.
FEATURES = [
    'RelPorteriaRivalX', 'RelPorteriaRivalZ',
    'RelPorteriaPropiaX', 'RelPorteriaPropiaZ',
    'TienePelota', 'RelPelotaX', 'RelPelotaZ', 'DistPelota', 'TienePelotaEquipo',
    'DistPorteriaContraria', 'DistPorteriaPropia',
    'PuntuacionPropia', 'PuntuacionContraria',
    'DistPelotaPorteriaPropia', 'DistAliadoCercano', 'DistEnemigoCercano',
    'RelAliado1PosX', 'RelAliado1PosZ', 'Aliado1DirX', 'Aliado1DirZ',
    'RelAliado2PosX', 'RelAliado2PosZ', 'Aliado2DirX', 'Aliado2DirZ',
    'RelAliado3PosX', 'RelAliado3PosZ', 'Aliado3DirX', 'Aliado3DirZ',
    'RelEnemigo1PosX', 'RelEnemigo1PosZ', 'Enemigo1DirX', 'Enemigo1DirZ',
    'RelEnemigo2PosX', 'RelEnemigo2PosZ', 'Enemigo2DirX', 'Enemigo2DirZ',
    'RelEnemigo3PosX', 'RelEnemigo3PosZ', 'Enemigo3DirX', 'Enemigo3DirZ',
]
assert len(FEATURES) == 40


def tramos_continuos(t):
    """
    El filtro de spawn borra filas, asi que dos filas seguidas del CSV limpio
    no siempre estan a 0.1 s. Aqui se parte en tramos donde SI lo estan: una
    secuencia que cruce un hueco le enseñaria a la red una fisica que no
    existe.
    """
    cortes = np.flatnonzero(np.diff(t) != 1) + 1
    return [(a, b) for a, b in zip(np.r_[0, cortes], np.r_[cortes, len(t)]) if b > a]


def pesos_positivos(Ya, Yg):
    pw = torch.zeros(N_ACC)
    for i, nom in enumerate(ACCIONES):
        dentro = Yg[:, i] > 0.5
        n = int(dentro.sum())
        pos = max(float((Ya[dentro, i] > 0.5).sum()), 1.0)
        neg = max(n - pos, 1.0)
        pw[i] = min(neg / pos, 50.0)
        print(f"  pos_weight[{nom}] = {pw[i]:.1f}  ({int(pos)} positivos / "
              f"{int(neg)} negativos, sobre {n} frames con la puerta abierta)")
    return pw


# =============================================================== evaluacion
def informe(nombre, pred_mov, true_mov, prob_acc, true_acc, gate):
    print('\n' + '=' * 62)
    print(f'  EVALUACION — {nombre} (test 20%)')
    print('=' * 62)

    acc_mov = accuracy_score(true_mov, pred_mov)
    f1_mov = f1_score(true_mov, pred_mov, average='macro', zero_division=0)
    prec_mov = precision_score(true_mov, pred_mov, average='macro', zero_division=0)
    rec_mov = recall_score(true_mov, pred_mov, average='macro', zero_division=0)
    print(f'\n[Movimiento] Accuracy={acc_mov*100:.1f}%  Prec={prec_mov*100:.1f}%  '
          f'Rec={rec_mov*100:.1f}%  F1={f1_mov*100:.1f}%')

    m = {'acc_mov': acc_mov * 100, 'f1_mov': f1_mov * 100}
    print('\n[Acciones — cada una solo en los frames donde era posible]')
    for i, nom in enumerate(ACCIONES):
        msk = gate[:, i] > 0.5
        n, pos = int(msk.sum()), int(true_acc[msk, i].sum()) if msk.sum() else 0
        if n == 0 or pos == 0:
            print(f'  {nom:8} sin muestras evaluables ({n} frames, {pos} positivos)')
            m['f1_' + nom.lower()] = 0.0
            continue
        p = (prob_acc[msk, i] >= 0.5).astype(int)
        v = true_acc[msk, i].astype(int)
        f1 = f1_score(v, p, zero_division=0) * 100
        mejor_t, mejor_f1 = 0.5, -1.0
        for t in np.arange(0.05, 0.96, 0.05):
            f = f1_score(v, (prob_acc[msk, i] >= t).astype(int), zero_division=0)
            if f > mejor_f1:
                mejor_f1, mejor_t = f, t
        print(f'  {nom:8} {n:5} frames, {pos:4} positivos ({pos/n*100:4.1f}%)  '
              f'F1@0.50={f1:5.1f}%   mejor umbral {mejor_t:.2f} -> F1={mejor_f1*100:.1f}%')
        m['f1_' + nom.lower()] = f1
        m['umbral_' + nom.lower()] = float(mejor_t)
    return m


# ===================================================================== main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--csv', required=True)
    ap.add_argument('--nombre', default='10minutos')
    ap.add_argument('--salida', default='.')
    ap.add_argument('--epocas', type=int, default=EPOCAS)
    ap.add_argument('--semillas', type=int, default=3)
    a = ap.parse_args()

    torch.manual_seed(42)
    np.random.seed(42)
    os.makedirs(a.salida, exist_ok=True)

    print('=' * 62)
    print('  GRU Y FNN SLIDING WINDOW — dataset unico')
    print('=' * 62)

    # El numero de fotograma original viaja como columna para poder detectar
    # los huecos que deja el filtro de spawn.
    crudo = pd.read_csv(a.csv)
    crudo['_t'] = np.arange(len(crudo))
    tmp = os.path.join(a.salida, '_tmp_seq.csv')
    crudo.to_csv(tmp, index=False)
    print(f'\n{os.path.basename(a.csv)}: {len(crudo)} filas '
          f'({len(crudo)*0.1/60:.1f} min)')

    base = F.SoccerDataset([tmp], aplicar_smote=False)
    limpio = base.raw_limpio
    os.remove(tmp)

    tr, te = F.dividir_sin_fuga(limpio, test_size=0.2)
    Xtr, _, Mtr, Atr, Gtr, mean, std = tensores(tr)
    Xte, _, Mte, Ate, Gte, _, _ = tensores(te, mean, std)
    ttr, tte = tr['_t'].values, te['_t'].values
    print(f'\n  train {len(tr)} frames | test {len(te)} frames')
    print(f'  tramos continuos: {len(tramos_continuos(ttr))} en train, '
          f'{len(tramos_continuos(tte))} en test')

    print('\n[pesos de clase]')
    pw = pesos_positivos(Atr, Gtr)

    # Una sola ejecucion no vale: ya medimos que cambiar la semilla mueve el F1
    # de las acciones hasta diecisiete puntos. Se entrenan varias, se informa de
    # la media y la dispersion, y se exporta la mediana en exactitud de
    # movimiento — ni la mejor ni la peor, la representativa.
    resultados = {}
    for etiqueta, fn in [('GRU', entrenar_gru), ('SlidingWindow', entrenar_ventana)]:
        corridas = []
        for s_ in range(a.semillas):
            print(f'\n### {etiqueta} — semilla {s_}')
            torch.manual_seed(s_); np.random.seed(s_)
            corridas.append(fn(Xtr, Mtr, Atr, Gtr, ttr, Xte, Mte, Ate, Gte, tte, pw, a))
        resultados[etiqueta] = resumir(etiqueta, corridas, a)

    # Escaladores para Unity: 40 valores para la GRU, 80 para la ventana (las
    # mismas estadisticas repetidas, porque las dos mitades son las mismas
    # variables en dos instantes).
    guardar_scaler(os.path.join(a.salida, f'scaler_gru_{a.nombre}.json'), mean, std)
    guardar_scaler(os.path.join(a.salida, f'scaler_sliding_{a.nombre}.json'),
                   np.tile(mean, 2), np.tile(std, 2))

    with open(os.path.join(a.salida, f'metrics_secuenciales_{a.nombre}.json'), 'w') as f:
        json.dump(resultados, f, indent=2)

    print('\n' + '=' * 62)
    print('  RESUMEN')
    print('=' * 62)
    print(f"\n  Media +/- desviacion entre {a.semillas} semillas.\n")
    print(f"{'arquitectura':16} {'accMov':>13} {'f1Mov':>13} {'f1Disp':>13} "
          f"{'f1Pase':>13} {'f1RoboK':>13} {'f1RoboL':>13}")
    print('-' * 100)
    for n, m in resultados.items():
        fila = ' '.join(f"{m[k]:6.1f}+/-{m[k+'_sd']:4.1f}" for k in
                        ['acc_mov', 'f1_mov', 'f1_disparo', 'f1_pase', 'f1_robok', 'f1_robol'])
        print(f"{n:16} {fila}")
    print("\n  Referencia, el FNN entrenado con este mismo CSV (meta_10minutos.json):")
    print("  FNN dual-head      36.6         26.0         16.8         37.5"
          "         11.1          0.0   (una sola semilla)")


def resumir(etiqueta, corridas, a):
    """Media y desviacion entre semillas, y exportacion del modelo mediano."""
    claves = ['acc_mov', 'f1_mov', 'f1_disparo', 'f1_pase', 'f1_robok', 'f1_robol']
    m = {}
    for k in claves:
        v = np.array([c[0][k] for c in corridas], float)
        m[k] = float(v.mean())
        m[k + '_sd'] = float(v.std(ddof=1)) if len(v) > 1 else 0.0
    m['semillas'] = len(corridas)
    m['loss_final'] = float(np.mean([c[0]['loss_final'] for c in corridas]))

    accs = [c[0]['acc_mov'] for c in corridas]
    elegido = int(np.argsort(accs)[len(accs) // 2])
    m['semilla_exportada'] = elegido
    print(f'\n  [{etiqueta}] semillas: acc_mov ' +
          ', '.join(f'{x:.1f}' for x in accs) +
          f'  -> se exporta la semilla {elegido}')
    corridas[elegido][1](a)                      # llama al exportador guardado
    return m


def guardar_scaler(ruta, mean, std):
    with open(ruta, 'w') as f:
        json.dump({'mean': np.asarray(mean, float).tolist(),
                   'std': np.asarray(std, float).tolist()}, f)
    print(f'  scaler -> {ruta}  ({len(np.asarray(mean))} valores)')


# ------------------------------------------------------------------ GRU
def entrenar_gru(Xtr, Mtr, Atr, Gtr, ttr, Xte, Mte, Ate, Gte, tte, pw, a):
    print('\n' + '=' * 62)
    print('  GRU  (estado oculto de 64, perdida en todos los pasos)')
    print('=' * 62)

    trozos = []
    for ini, fin in tramos_continuos(ttr):
        for s in range(ini, fin - SEQ_GRU + 1, PASO_GRU):
            trozos.append((s, s + SEQ_GRU))
    if not trozos:
        raise SystemExit('No hay ningun tramo continuo de 50 fotogramas.')
    print(f'  {len(trozos)} trozos de {SEQ_GRU} pasos')

    X = torch.tensor(Xtr); M = torch.tensor(Mtr)
    A = torch.tensor(Atr); G = torch.tensor(Gtr)
    idx = torch.tensor([[s + k for k in range(SEQ_GRU)] for s, _ in trozos])

    modelo = SoccerGRU(len(FEATURES))
    ce = nn.CrossEntropyLoss()
    bce = nn.BCEWithLogitsLoss(pos_weight=pw, reduction='none')
    opt = optim.Adam(modelo.parameters(), lr=LR)

    print(f'\nEntrenando {a.epocas} epocas...')
    ultima = 0.0
    for ep in range(a.epocas):
        modelo.train()
        orden = torch.randperm(len(idx))
        total = n = 0.0
        for b in range(0, len(orden), BATCH_GRU):
            sel = idx[orden[b:b + BATCH_GRU]]
            opt.zero_grad()
            lm, la, _ = modelo(X[sel])                      # [B,T,9] [B,T,4]
            l_ce = ce(lm.reshape(-1, N_MOV), M[sel].reshape(-1))
            el = bce(la.reshape(-1, N_ACC), A[sel].reshape(-1, N_ACC))
            g = G[sel].reshape(-1, N_ACC)
            l_bce = (el * g).sum() / g.sum().clamp(min=1.0)
            loss = PESO_MOV * l_ce + PESO_ACC * l_bce
            loss.backward()
            torch.nn.utils.clip_grad_norm_(modelo.parameters(), 5.0)
            opt.step()
            total += float(loss); n += 1
        ultima = total / max(n, 1)
        if (ep + 1) % 25 == 0:
            print(f'  Epoca [{ep+1:3d}/{a.epocas}]  Loss={ultima:.4f}')

    # Evaluacion: se recorre el test como en el juego, arrastrando el estado
    # oculto dentro de cada tramo continuo y reiniciandolo en los cortes.
    modelo.eval()
    pm, pa = [], []
    with torch.no_grad():
        Xt = torch.tensor(Xte)
        for ini, fin in tramos_continuos(tte):
            h = torch.zeros(1, OCULTO)
            lm, la, _ = modelo(Xt[ini:fin].unsqueeze(0), h)
            pm.append(lm[0].argmax(-1).numpy())
            pa.append(torch.sigmoid(la[0]).numpy())
    pm = np.concatenate(pm); pa = np.vstack(pa)
    orden_te = np.concatenate([np.arange(i, f) for i, f in tramos_continuos(tte)])
    m = informe('GRU', pm, Mte[orden_te], pa, Ate[orden_te], Gte[orden_te])
    m['loss_final'] = ultima

    def exportar(aa):
        ruta = os.path.join(aa.salida, f'SoccerModelGRU_{aa.nombre}.onnx')
        exp = GRUPaso(modelo).eval()
        torch.onnx.export(
            exp, (torch.randn(1, 1, len(FEATURES)), torch.zeros(1, 1, OCULTO)), ruta,
            export_params=True, opset_version=14, do_constant_folding=True,
            input_names=['vector_observation', 'hidden_state_in'],
            output_names=['continuous_actions', 'discrete_actions', 'hidden_state_out'],
            dynamo=False)
        print(f'  ONNX -> {ruta}')

    return m, exportar


# -------------------------------------------------------- sliding window
def entrenar_ventana(Xtr, Mtr, Atr, Gtr, ttr, Xte, Mte, Ate, Gte, tte, pw, a):
    print('\n' + '=' * 62)
    print('  FNN SLIDING WINDOW  (fotograma anterior + actual = 80 entradas)')
    print('=' * 62)

    def pares(X, t):
        fin = []
        for ini, f in tramos_continuos(t):
            fin += list(range(ini + 1, f))       # i necesita i-1 en el mismo tramo
        fin = np.array(fin, dtype=int)
        return np.concatenate([X[fin - 1], X[fin]], 1), fin

    Vtr, ftr = pares(Xtr, ttr)
    Vte, fte = pares(Xte, tte)
    print(f'  {len(Vtr)} pares de entrenamiento, {len(Vte)} de test')

    X = torch.tensor(Vtr); M = torch.tensor(Mtr[ftr])
    A = torch.tensor(Atr[ftr]); G = torch.tensor(Gtr[ftr])

    modelo = FNNVentana(2 * len(FEATURES))
    ce = nn.CrossEntropyLoss()
    bce = nn.BCEWithLogitsLoss(pos_weight=pw, reduction='none')
    opt = optim.Adam(modelo.parameters(), lr=LR)

    print(f'\nEntrenando {a.epocas} epocas...')
    ultima = 0.0
    for ep in range(a.epocas):
        modelo.train()
        orden = torch.randperm(len(X))
        total = n = 0.0
        for b in range(0, len(orden), BATCH):
            sel = orden[b:b + BATCH]
            opt.zero_grad()
            lm, la = modelo(X[sel])
            l_ce = ce(lm, M[sel])
            el = bce(la, A[sel])
            l_bce = (el * G[sel]).sum() / G[sel].sum().clamp(min=1.0)
            loss = PESO_MOV * l_ce + PESO_ACC * l_bce
            loss.backward()
            opt.step()
            total += float(loss); n += 1
        ultima = total / max(n, 1)
        if (ep + 1) % 25 == 0:
            print(f'  Epoca [{ep+1:3d}/{a.epocas}]  Loss={ultima:.4f}')

    modelo.eval()
    with torch.no_grad():
        lm, la = modelo(torch.tensor(Vte))
    m = informe('FNN sliding window', lm.argmax(-1).numpy(), Mte[fte],
                torch.sigmoid(la).numpy(), Ate[fte], Gte[fte])
    m['loss_final'] = ultima

    def exportar(aa):
        ruta = os.path.join(aa.salida, f'SoccerModelSliding_{aa.nombre}.onnx')
        exp = VentanaExport(modelo).eval()
        torch.onnx.export(
            exp, torch.randn(1, 2 * len(FEATURES)), ruta,
            export_params=True, opset_version=14, do_constant_folding=True,
            input_names=['vector_observation'],
            output_names=['continuous_actions', 'discrete_actions'],
            dynamic_axes={'vector_observation': {0: 'batch_size'},
                          'continuous_actions': {0: 'batch_size'},
                          'discrete_actions': {0: 'batch_size'}},
            dynamo=False)
        print(f'  ONNX -> {ruta}')

    return m, exportar


if __name__ == '__main__':
    main()
