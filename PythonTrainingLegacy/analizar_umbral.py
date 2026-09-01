"""
Analisis del umbral de activacion (actionThreshold)
===================================================
Responde a: que valor de umbral hace que el NPC actue con el mismo ritmo que
el humano que lo entreno, sin caer en el spam ni quedarse paralizado.

No usa F1. Maximizar F1 sobre una clase rara empuja el umbral hacia abajo, que
es justo lo contrario de lo que interesa aqui. En su lugar mide dos cosas:

  FIDELIDAD  el NPC pulsa cada tecla tantas veces por minuto como el humano.
             1.00 = ritmo identico. Penaliza igual pasarse que quedarse corto.

  ACIERTO    de las veces que el NPC pulsa, cuantas caen a menos de 0.5 s de
             una pulsacion real del humano en ese mismo punto de la partida.

El umbral recomendado es el que maximiza la media geometrica de ambas.

Simula el disparador REAL del juego: puerta de la accion + umbral + el
cooldown de 1 s de AIControllerFNNClasi, para que las pulsaciones/min sean
las que de verdad se veran en pantalla.

Uso:
    python analizar_umbral.py
    python analizar_umbral.py --csv "../Assets/SoccerData_*.csv"
"""
import argparse, glob, json, os
import numpy as np, pandas as pd, onnxruntime as ort

DT = 0.1            # el Recorder graba a 10 Hz
COOLDOWN = 1.0      # AIControllerFNNClasi.actionCooldown
TOL = 5             # +-5 frames = +-0.5 s para contar un acierto
RANGO_SPRINT = 600.0   # WinTheBall.rangoSprint
RANGO_ROBO = 100.0     # WinTheBall.distanciaMaxima

FEATS = ['RelPorteriaRivalX','RelPorteriaRivalZ','RelPorteriaPropiaX','RelPorteriaPropiaZ',
 'TienePelota','RelPelotaX','RelPelotaZ','DistPelota','TienePelotaEquipo',
 'DistPorteriaContraria','DistPorteriaPropia','PuntuacionPropia','PuntuacionContraria',
 'DistPelotaPorteriaPropia','DistAliadoCercano','DistEnemigoCercano',
 'RelAliado1PosX','RelAliado1PosZ','Aliado1DirX','Aliado1DirZ',
 'RelAliado2PosX','RelAliado2PosZ','Aliado2DirX','Aliado2DirZ',
 'RelAliado3PosX','RelAliado3PosZ','Aliado3DirX','Aliado3DirZ',
 'RelEnemigo1PosX','RelEnemigo1PosZ','Enemigo1DirX','Enemigo1DirZ',
 'RelEnemigo2PosX','RelEnemigo2PosZ','Enemigo2DirX','Enemigo2DirZ',
 'RelEnemigo3PosX','RelEnemigo3PosZ','Enemigo3DirX','Enemigo3DirZ']
ACTS = ['Disparo', 'Pase', 'RoboK', 'RoboL']
UMBRALES = np.arange(0.05, 0.96, 0.05)


def puertas(df):
    """Las mismas puertas que train_clasificacion_fnn.py: en que frames tiene
    sentido preguntar por cada accion."""
    tiene = df['TienePelota'] > 0.5
    rival = (~tiene) & (df['TienePelotaEquipo'] > 1.5)
    return np.column_stack([
        tiene.values,
        tiene.values,
        (rival & (df['DistPelota'] <= RANGO_SPRINT)).values,
        (rival & (df['DistPelota'] < RANGO_ROBO)).values,
    ]).astype(bool)


def pulsaciones(mask):
    """Frames en los que el juego pulsaria de verdad, aplicando el cooldown."""
    out, ultimo = [], -1e9
    for i in np.flatnonzero(mask):
        if (i - ultimo) * DT >= COOLDOWN:
            out.append(i); ultimo = i
    return np.array(out, dtype=int)


def pulsaciones_humanas(lab):
    """Flancos 0->1 de la etiqueta cruda: cada uno es una pulsacion real."""
    l = lab.astype(int)
    idx = np.flatnonzero((l[1:] == 1) & (l[:-1] == 0)) + 1
    return np.concatenate([[0], idx]) if l[0] == 1 else idx


def analizar(csv_glob, modelos_dir):
    csvs = sorted(glob.glob(csv_glob))
    if not csvs:
        raise SystemExit(f'No hay CSV que casen con {csv_glob}')
    raw = pd.concat([pd.read_csv(c) for c in csvs], ignore_index=True)
    G = puertas(raw)
    X = raw[FEATS].values.astype(np.float32)
    minutos = len(raw) * DT / 60
    H = {a: pulsaciones_humanas(raw[a].values) for a in ACTS}

    print(f'{len(csvs)} sesiones | {len(raw)} frames | {minutos:.1f} min de juego\n')
    print('RITMO DEL HUMANO')
    for a in ACTS:
        print(f'  {a:8} {len(H[a]):4} pulsaciones = {len(H[a])/minutos:5.2f}/min')

    onnxs = sorted(glob.glob(os.path.join(modelos_dir, 'SoccerModel_*.onnx')))
    for po in onnxs:
        nombre = os.path.basename(po)[len('SoccerModel_'):-len('.onnx')]
        ps = os.path.join(modelos_dir, f'scaler_{nombre}.json')
        if not os.path.exists(ps):
            continue
        sc = json.load(open(ps))
        mean = np.array(sc['mean'], np.float32)
        std = np.array(sc['std'], np.float32); std[std == 0] = 1.0
        try:
            _, act = ort.InferenceSession(po).run(None, {'vector_observation': (X - mean) / std})
        except Exception as e:
            print(f'\n[{nombre}] no se pudo evaluar: {e}'); continue

        print('\n' + '=' * 88)
        print(f'MODELO: {nombre}')
        print('=' * 88)
        print(f"{'umbral':>7} | {'ritmo vs humano (1.00 = igual)':^37} | {'acierto %':^23} | {'fid':>5} {'ac':>4} {'score':>6}")
        print(f"{'':>7} | " + ' '.join(f'{a:>8}' for a in ACTS) + ' | ' +
              ' '.join(f'{a:>5}' for a in ACTS) + ' |')
        mejor = (None, -1)
        for t in UMBRALES:
            r, p = [], []
            for j, a in enumerate(ACTS):
                pr = pulsaciones(G[:, j] & (act[:, j] >= t))
                obj = len(H[a]) / minutos
                r.append((len(pr) / minutos) / obj if obj > 0 else np.nan)
                p.append(np.nan if len(pr) == 0 else
                         sum(1 for i in pr if len(H[a]) and np.min(np.abs(H[a] - i)) <= TOL) / len(pr))
            r, p = np.array(r, float), np.array(p, float)
            fid = float(np.nanmean(np.exp(-np.abs(np.log(np.clip(r, 1e-3, None))))))
            ac = float(np.nanmean(p))
            score = float(np.sqrt(fid * ac)) if ac == ac else np.nan
            if score == score and score > mejor[1]:
                mejor = (t, score)
            print(f'{t:7.2f} | ' + ' '.join(f'{v:8.2f}' if v == v else '     -- ' for v in r) +
                  ' | ' + ' '.join(f'{v*100:5.0f}' if v == v else '   --' for v in p) +
                  f' | {fid:5.2f} {ac*100:3.0f}% ' +
                  (f'{score:6.3f}' if score == score else '    --'))
        if mejor[0] is not None:
            print(f'  => umbral recomendado para este modelo: {mejor[0]:.2f} (score {mejor[1]:.3f})')


if __name__ == '__main__':
    aqui = os.path.dirname(os.path.abspath(__file__))
    assets = os.path.join(os.path.dirname(aqui), 'Assets')
    ap = argparse.ArgumentParser()
    ap.add_argument('--csv', default=os.path.join(assets, 'SoccerData_*.csv'))
    ap.add_argument('--modelos', default=assets)
    a = ap.parse_args()
    analizar(a.csv, a.modelos)
