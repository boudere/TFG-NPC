"""
comun.py - Protocolo unico de evaluacion para la comparativa de arquitecturas
=============================================================================
Todas las arquitecturas del TFG (FNN, MoE, RNN, GRU, sliding window, FSM) se
entrenan y se miden con ESTE modulo, para que la tabla comparativa sea honesta.

Que hace distinto de los scripts originales:

  1. Particion POR SESION, no aleatoria. A 10 Hz dos fotogramas consecutivos
     son casi el mismo estado; una particion aleatoria coloca fotogramas
     gemelos a los dos lados y el modelo aprueba un examen que ya ha visto.
  2. Sin SMOTE. Se aplicaba antes de partir, asi que muestras sinteticas
     derivadas del entrenamiento acababan en el test.
  3. F1 en lugar de exactitud para las acciones, y evaluadas solo dentro de
     su contexto (disparo y pase solo cuando el agente tiene el balon).
  4. Lineas base (azar, clase mayoritaria, persistencia) para que las cifras
     se puedan interpretar.

La comparativa se limita a Disparo y Pase: son las dos acciones que las seis
arquitecturas soportaban en su version original, asi que ninguna sale
beneficiada por haberse escrito despues.
"""
import glob
import os

import numpy as np
import pandas as pd
import torch
from sklearn.metrics import f1_score, accuracy_score, precision_score, recall_score

SEMILLA = 42
DT = 0.1                 # el Recorder graba a 10 Hz
LOOKBACK = 5             # fotogramas hacia atras para recolocar una etiqueta
VENTANA_INTENCION = 5    # 0.5 s previos a la accion cuentan como intencion

FEATURES = [
    'RelPorteriaRivalX', 'RelPorteriaRivalZ',
    'RelPorteriaPropiaX', 'RelPorteriaPropiaZ',
    'TienePelota', 'RelPelotaX', 'RelPelotaZ', 'DistPelota', 'TienePelotaEquipo',
    'DistPorteriaContraria', 'DistPorteriaPropia', 'PuntuacionPropia', 'PuntuacionContraria',
    'DistPelotaPorteriaPropia', 'DistAliadoCercano', 'DistEnemigoCercano',
    'RelAliado1PosX', 'RelAliado1PosZ', 'Aliado1DirX', 'Aliado1DirZ',
    'RelAliado2PosX', 'RelAliado2PosZ', 'Aliado2DirX', 'Aliado2DirZ',
    'RelAliado3PosX', 'RelAliado3PosZ', 'Aliado3DirX', 'Aliado3DirZ',
    'RelEnemigo1PosX', 'RelEnemigo1PosZ', 'Enemigo1DirX', 'Enemigo1DirZ',
    'RelEnemigo2PosX', 'RelEnemigo2PosZ', 'Enemigo2DirX', 'Enemigo2DirZ',
    'RelEnemigo3PosX', 'RelEnemigo3PosZ', 'Enemigo3DirX', 'Enemigo3DirZ',
]
ACCIONES = ['Disparo', 'Pase']
N_MOV = 9


def fijar_semilla(s=SEMILLA):
    np.random.seed(s)
    torch.manual_seed(s)


# ===================================================================== datos
def cargar_sesiones(patrones):
    """Una sesion = una partida grabada. Se descartan los CSV con esquema
    antiguo: son de versiones anteriores del juego y del Recorder."""
    rutas = []
    for p in patrones:
        rutas += sorted(glob.glob(p))
    vistos, sesiones = set(), []
    for r in rutas:
        n = os.path.basename(r)
        if n in vistos:
            continue
        vistos.add(n)
        d = pd.read_csv(r)
        faltan = [c for c in FEATURES + ACCIONES if c not in d.columns]
        if faltan:
            print(f"  {n}: DESCARTADO (esquema antiguo, falta {faltan[0]})")
            continue
        d['_sesion'] = n
        sesiones.append(preparar(d))
        print(f"  {n}: {len(d)} filas")
    return sesiones


def preparar(raw):
    """Correccion de timing + ventana de intencion + clase de movimiento."""
    raw = raw.reset_index(drop=True).copy()

    # Al pulsar disparo o pase el balon sale en el mismo fotograma, asi que la
    # etiqueta cae donde TienePelota ya vale 0. Se recoloca hacia atras hasta
    # el ultimo fotograma con balon; si no lo hay, se descarta.
    for col in ACCIONES:
        for idx in raw.index[raw[col] == 1].tolist():
            if raw.loc[idx, 'TienePelota'] == 1:
                continue
            movida = False
            for off in range(1, LOOKBACK + 1):
                j = idx - off
                if j < 0:
                    break
                if raw.loc[j, 'TienePelota'] == 1:
                    raw.loc[idx, col] = 0
                    raw.loc[j, col] = 1
                    movida = True
                    break
            if not movida:
                raw.loc[idx, col] = 0

    # Los fotogramas previos a una accion son estados en los que el jugador ya
    # iba a ejecutarla, y estaban etiquetados al reves.
    for col in ACCIONES:
        m = raw[col].values.copy()
        for i in raw.index[raw[col] == 1].tolist():
            for off in range(1, VENTANA_INTENCION):
                j = i - off
                if j < 0:
                    break
                m[j] = 1
        raw[col] = m

    dx = np.where(raw['InputX'] > 0.5, 1, np.where(raw['InputX'] < -0.5, -1, 0))
    dz = np.where(raw['InputZ'] > 0.5, 1, np.where(raw['InputZ'] < -0.5, -1, 0))
    raw['_mov'] = (dx + 1) * 3 + (dz + 1)
    raw['_puerta'] = (raw['TienePelota'] > 0.5).astype(int)
    return raw


def dividir(sesiones, n_test=None):
    """Las sesiones mas recientes van a test. Nunca se parte una sesion."""
    if len(sesiones) < 2:
        s = sesiones[0]
        corte = int(len(s) * 0.8)
        print("  AVISO: una sola sesion, se usa particion temporal 80/20.")
        return [s.iloc[:corte].reset_index(drop=True)], [s.iloc[corte:].reset_index(drop=True)]
    if n_test is None:
        n_test = max(1, len(sesiones) // 4)
    return sesiones[:-n_test], sesiones[-n_test:]


def escalador(train):
    """Media y desviacion SOLO del entrenamiento."""
    X = np.concatenate([s[FEATURES].values for s in train]).astype(np.float32)
    mean = X.mean(0)
    std = X.std(0)
    std[std < 1e-6] = 1.0
    return mean, std


# ================================================================= metricas
def evaluar(y_mov, p_mov, y_act, p_act, puerta, prev_mov=None):
    """
    y_mov, p_mov : clase de movimiento real y predicha (enteros 0-8)
    y_act, p_act : [N, 2] etiquetas y probabilidades de Disparo y Pase
    puerta       : [N] booleano, True donde el agente tiene el balon
    prev_mov     : clase del fotograma anterior, para la linea base
    """
    r = {
        'acc_mov': accuracy_score(y_mov, p_mov) * 100,
        'f1_mov': f1_score(y_mov, p_mov, average='macro', zero_division=0) * 100,
        'prec_mov': precision_score(y_mov, p_mov, average='macro', zero_division=0) * 100,
        'rec_mov': recall_score(y_mov, p_mov, average='macro', zero_division=0) * 100,
    }
    for j, nombre in enumerate(ACCIONES):
        g = puerta
        if g.sum() == 0 or y_act[g, j].sum() == 0:
            r[f'f1_{nombre.lower()}'] = float('nan')
            r[f'n_{nombre.lower()}'] = int(y_act[g, j].sum()) if g.sum() else 0
            continue
        pred = (p_act[g, j] >= 0.5).astype(int)
        r[f'f1_{nombre.lower()}'] = f1_score(y_act[g, j].astype(int), pred, zero_division=0) * 100
        r[f'n_{nombre.lower()}'] = int(y_act[g, j].sum())
    if prev_mov is not None:
        r['base_persistencia'] = accuracy_score(y_mov, prev_mov) * 100
    vals, cnt = np.unique(y_mov, return_counts=True)
    r['base_mayoritaria'] = cnt.max() / len(y_mov) * 100
    r['base_azar'] = 100.0 / N_MOV
    return r


def lineas_base(test):
    """Las tres referencias que hacen interpretable cualquier cifra."""
    y = np.concatenate([s['_mov'].values for s in test])
    prev = np.concatenate([np.concatenate([[s['_mov'].values[0]], s['_mov'].values[:-1]])
                           for s in test])
    vals, cnt = np.unique(y, return_counts=True)
    return {
        'Azar (9 clases)': 100.0 / N_MOV,
        'Clase mayoritaria': cnt.max() / len(y) * 100,
        'Persistencia (repetir anterior)': accuracy_score(y, prev) * 100,
    }
