"""
comparar.py - Comparativa de las seis arquitecturas del TFG
===========================================================
Entrena y evalua FNN dual-head, multi-modelo (MoE), RNN, GRU, FNN sliding
window e inspirado en FSM sobre EXACTAMENTE los mismos datos, la misma
particion, el mismo escalador y la misma semilla.

Es una comparativa offline: no necesita Unity ni exportar a ONNX. Lo que se
compara es la arquitectura, no la integracion.

Uso:
    python comparar.py
    python comparar.py --epocas 150 --datos "../Assets/SoccerData_*.csv"
"""
import argparse
import os
import sys

import numpy as np
import pandas as pd
import torch
import torch.nn as nn
from torch.utils.data import TensorDataset, DataLoader

import comun
from comun import FEATURES, ACCIONES, N_MOV
import arquitecturas as arq

LR = 1e-3
BATCH = 32
PESO_ACCION = 2.0     # las acciones pesan el doble: son criticas y minoritarias
DEV = 'cuda' if torch.cuda.is_available() else 'cpu'


# ------------------------------------------------------------ construccion
def _mat(sesiones, mean, std):
    X = np.concatenate([s[FEATURES].values for s in sesiones]).astype(np.float32)
    return (X - mean) / std


def _etq(sesiones):
    ym = np.concatenate([s['_mov'].values for s in sesiones]).astype(np.int64)
    ya = np.concatenate([s[ACCIONES].values for s in sesiones]).astype(np.float32)
    g = np.concatenate([s['_puerta'].values for s in sesiones]).astype(bool)
    return ym, ya, g


def _secuencias(sesiones, mean, std, seq):
    """Ventanas de `seq` fotogramas que NO cruzan de sesion."""
    Xs, ym, ya, g = [], [], [], []
    for s in sesiones:
        X = (s[FEATURES].values.astype(np.float32) - mean) / std
        m, a, p = s['_mov'].values, s[ACCIONES].values, s['_puerta'].values
        for i in range(seq - 1, len(s)):
            Xs.append(X[i - seq + 1:i + 1])
            ym.append(m[i]); ya.append(a[i]); g.append(p[i])
    return (np.asarray(Xs, np.float32), np.asarray(ym, np.int64),
            np.asarray(ya, np.float32), np.asarray(g, bool))


def _ventana(sesiones, mean, std):
    """Fotograma actual concatenado con el anterior, sin cruzar de sesion."""
    Xs, ym, ya, g = [], [], [], []
    for s in sesiones:
        X = (s[FEATURES].values.astype(np.float32) - mean) / std
        m, a, p = s['_mov'].values, s[ACCIONES].values, s['_puerta'].values
        for i in range(1, len(s)):
            Xs.append(np.concatenate([X[i], X[i - 1]]))
            ym.append(m[i]); ya.append(a[i]); g.append(p[i])
    return (np.asarray(Xs, np.float32), np.asarray(ym, np.int64),
            np.asarray(ya, np.float32), np.asarray(g, bool))


def _pos_weight(ya):
    """Peso positivo por accion, segun su propio desbalance."""
    w = []
    for j in range(ya.shape[1]):
        pos = ya[:, j].sum()
        neg = len(ya) - pos
        w.append(neg / pos if pos > 0 else 1.0)
    return torch.tensor(np.clip(w, 1.0, 50.0), dtype=torch.float32, device=DEV)


# ---------------------------------------------------------------- entreno
def entrenar_dual(modelo, X, ym, ya, epocas, pw):
    modelo.to(DEV).train()
    opt = torch.optim.Adam(modelo.parameters(), lr=LR)
    ce, bce = nn.CrossEntropyLoss(), nn.BCEWithLogitsLoss(pos_weight=pw)
    dl = DataLoader(TensorDataset(torch.tensor(X), torch.tensor(ym), torch.tensor(ya)),
                    batch_size=BATCH, shuffle=True)
    ult = 0.0
    for _ in range(epocas):
        tot = 0.0
        for xb, mb, ab in dl:
            xb, mb, ab = xb.to(DEV), mb.to(DEV), ab.to(DEV)
            opt.zero_grad()
            pm, pa = modelo(xb)
            loss = ce(pm, mb) + PESO_ACCION * bce(pa, ab)
            loss.backward(); opt.step()
            tot += loss.item()
        ult = tot / max(1, len(dl))
    return ult


def entrenar_fsm(modelo, X, est, mov, epocas):
    modelo.to(DEV).train()
    opt = torch.optim.Adam(modelo.parameters(), lr=LR)
    ce, mse = nn.CrossEntropyLoss(), nn.MSELoss()
    dl = DataLoader(TensorDataset(torch.tensor(X), torch.tensor(est), torch.tensor(mov)),
                    batch_size=BATCH, shuffle=True)
    ult = 0.0
    for _ in range(epocas):
        tot = 0.0
        for xb, eb, mb in dl:
            xb, eb, mb = xb.to(DEV), eb.to(DEV), mb.to(DEV)
            opt.zero_grad()
            pe, pm = modelo(xb)
            loss = ce(pe, eb) + mse(pm, mb)
            loss.backward(); opt.step()
            tot += loss.item()
        ult = tot / max(1, len(dl))
    return ult


@torch.no_grad()
def predecir(modelo, X):
    modelo.eval()
    outs = [[], []]
    for i in range(0, len(X), 512):
        a, b = modelo(torch.tensor(X[i:i + 512]).to(DEV))
        outs[0].append(a.cpu().numpy()); outs[1].append(b.cpu().numpy())
    return np.concatenate(outs[0]), np.concatenate(outs[1])


def _sig(z):
    # np.clip evita el RuntimeWarning de overflow con logits muy negativos.
    # El resultado no cambia: exp(50) ya satura el float32.
    return 1.0 / (1.0 + np.exp(-np.clip(z, -50.0, 50.0)))


def _disc(v):
    """Movimiento continuo -> las mismas 9 clases, para poder comparar."""
    dx = np.where(v[:, 0] > 0.33, 1, np.where(v[:, 0] < -0.33, -1, 0))
    dz = np.where(v[:, 1] > 0.33, 1, np.where(v[:, 1] < -0.33, -1, 0))
    return (dx + 1) * 3 + (dz + 1)


# ------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--datos', nargs='+', default=None)
    ap.add_argument('--epocas', type=int, default=150)
    ap.add_argument('--salida', default='comparativa_arquitecturas.csv')
    a = ap.parse_args()

    aqui = os.path.dirname(os.path.abspath(__file__))
    if a.datos is None:
        a.datos = [os.path.join(aqui, '..', 'Assets', 'SoccerData_*.csv')]

    print('=' * 78)
    print('  COMPARATIVA DE ARQUITECTURAS - protocolo unico, sin fuga')
    print('=' * 78)
    print(f'\n[1/4] Cargando sesiones   (dispositivo: {DEV})')
    sesiones = comun.cargar_sesiones(a.datos)
    if not sesiones:
        sys.exit('No hay sesiones con el esquema actual.')

    train, test = comun.dividir(sesiones)
    n_tr = sum(len(s) for s in train); n_te = sum(len(s) for s in test)
    print(f'\n  train: {len(train)} sesiones, {n_tr} filas ({n_tr*comun.DT/60:.1f} min)')
    print(f'  test : {len(test)} sesiones, {n_te} filas ({n_te*comun.DT/60:.1f} min)')

    mean, std = comun.escalador(train)

    print('\n[2/4] Lineas base sobre el conjunto de test')
    for k, v in comun.lineas_base(test).items():
        print(f'  {k:34} {v:5.1f}%')

    Xtr, ymtr, yatr, gtr = _mat(train, mean, std), *_etq(train)
    Xte, ymte, yate, gte = _mat(test, mean, std), *_etq(test)
    prev_te = np.concatenate([np.concatenate([[s['_mov'].values[0]], s['_mov'].values[:-1]])
                              for s in test])
    pw = _pos_weight(yatr)

    print(f'\n[3/4] Entrenando las seis arquitecturas ({a.epocas} epocas, Adam {LR}, lote {BATCH})')
    filas = []

    for nombre, cfg in arq.CATALOGO.items():
        comun.fijar_semilla()
        print(f'\n  --- {nombre}')
        tipo = cfg['tipo']

        if tipo == 'frame':
            m = cfg['clase'](cfg['entrada'])
            loss = entrenar_dual(m, Xtr, ymtr, yatr, a.epocas, pw)
            lm, la = predecir(m, Xte)
            pm, pa, yv, av, gv, pv = lm.argmax(1), _sig(la), ymte, yate, gte, prev_te
            params = sum(p.numel() for p in m.parameters())

        elif tipo == 'ventana':
            Xa, yma, yaa, ga = _ventana(train, mean, std)
            Xb, ymb, yab, gb = _ventana(test, mean, std)
            m = cfg['clase'](cfg['entrada'])
            loss = entrenar_dual(m, Xa, yma, yaa, a.epocas, _pos_weight(yaa))
            lm, la = predecir(m, Xb)
            pm, pa, yv, av, gv = lm.argmax(1), _sig(la), ymb, yab, gb
            pv = np.concatenate([[ymb[0]], ymb[:-1]])
            params = sum(p.numel() for p in m.parameters())

        elif tipo == 'secuencia':
            Xa, yma, yaa, ga = _secuencias(train, mean, std, cfg['seq'])
            Xb, ymb, yab, gb = _secuencias(test, mean, std, cfg['seq'])
            m = cfg['clase'](cfg['entrada'])
            loss = entrenar_dual(m, Xa, yma, yaa, a.epocas, _pos_weight(yaa))
            lm, la = predecir(m, Xb)
            pm, pa, yv, av, gv = lm.argmax(1), _sig(la), ymb, yab, gb
            pv = np.concatenate([[ymb[0]], ymb[:-1]])
            params = sum(p.numel() for p in m.parameters())

        elif tipo == 'fsm':
            est_tr = arq.estado_fsm(pd.concat(train, ignore_index=True))
            est_te = arq.estado_fsm(pd.concat(test, ignore_index=True))
            mov_tr = np.concatenate([s[['InputX', 'InputZ']].values for s in train]).astype(np.float32)
            m = cfg['clase'](cfg['entrada'])
            loss = entrenar_fsm(m, Xtr, est_tr, mov_tr, a.epocas)
            le, lv = predecir(m, Xte)
            pm = _disc(lv)
            pe = le.argmax(1)
            # El estado predicho es la accion: Pasando -> Pase, Tirando -> Disparo
            pa = np.zeros((len(pe), 2), np.float32)
            pa[pe == 3, 0] = 1.0
            pa[pe == 2, 1] = 1.0
            yv, av, gv, pv = ymte, yate, gte, prev_te
            params = sum(p.numel() for p in m.parameters())
            r_est = (pe == est_te).mean() * 100

        elif tipo == 'moe':
            rt_tr = arq.enrutar(pd.concat(train, ignore_index=True))
            rt_te = arq.enrutar(pd.concat(test, ignore_index=True))
            pm = np.zeros(len(Xte), np.int64)
            pa = np.zeros((len(Xte), 2), np.float32)
            params = 0
            usados = []
            for k, exp in enumerate(arq.EXPERTOS):
                sel = rt_tr == k
                if sel.sum() < 50:
                    print(f'      experto {exp}: solo {int(sel.sum())} frames de entreno, se omite')
                    continue
                comun.fijar_semilla()
                me = cfg['clase'](cfg['entrada'])
                entrenar_dual(me, Xtr[sel], ymtr[sel], yatr[sel], a.epocas, _pos_weight(yatr[sel]))
                params += sum(p.numel() for p in me.parameters())
                usados.append(exp)
                dst = rt_te == k
                if dst.sum() == 0:
                    print(f'      experto {exp}: entrenado con {int(sel.sum())} frames, '
                          f'pero el enrutado de inferencia NUNCA lo alcanza en test')
                    continue
                lm, la = predecir(me, Xte[dst])
                pm[dst] = lm.argmax(1); pa[dst] = _sig(la)
            print(f'      expertos entrenados: {", ".join(usados)}')
            loss = float('nan')
            yv, av, gv, pv = ymte, yate, gte, prev_te

        r = comun.evaluar(yv, pm, av, pa, gv, pv)
        r['arquitectura'] = nombre
        r['parametros'] = params
        r['loss'] = loss
        if tipo == 'fsm':
            r['acc_estado_fsm'] = r_est
        filas.append(r)
        print(f'      acc mov {r["acc_mov"]:5.1f}%   F1 mov {r["f1_mov"]:5.1f}%   '
              f'F1 disparo {r["f1_disparo"]:5.1f}%   F1 pase {r["f1_pase"]:5.1f}%')

    print('\n[4/4] Tabla comparativa')
    df = pd.DataFrame(filas)
    cols = ['arquitectura', 'parametros', 'acc_mov', 'f1_mov', 'prec_mov', 'rec_mov',
            'f1_disparo', 'f1_pase', 'n_disparo', 'n_pase']
    df = df[[c for c in cols if c in df.columns]]
    print()
    print(df.to_string(index=False, float_format=lambda v: f'{v:.1f}'))
    df.to_csv(a.salida, index=False)
    print(f'\nGuardado en {a.salida}')

    base = comun.lineas_base(test)
    mejor = df.loc[df['acc_mov'].idxmax()]
    print(f"\nMejor en movimiento: {mejor['arquitectura']} ({mejor['acc_mov']:.1f}%)")
    print(f"Persistencia:        {base['Persistencia (repetir anterior)']:.1f}%")
    if mejor['acc_mov'] < base['Persistencia (repetir anterior)']:
        print("  -> Ninguna arquitectura supera a repetir el fotograma anterior.")


if __name__ == '__main__':
    main()
