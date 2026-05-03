"""
train_rf_baseline.py — Baseline de Random Forest para comparación con las redes neuronales.

Entrena dos modelos RF sobre los mismos datos que los modelos neuronales:
  - RandomForestClassifier  → acciones discretas (Disparo, Pase, movimiento discretizado)
  - RandomForestRegressor   → movimiento continuo (InputX, InputZ)

Genera un informe de métricas comparable al METRICS_BLOCK de los modelos neurales.

Uso:
    cd TFG-NPC
    python PythonTraining\train_rf_baseline.py
"""

import glob
import json
import os
import sys
import numpy as np
import pandas as pd
from datetime import datetime
from sklearn.ensemble import RandomForestClassifier, RandomForestRegressor
from sklearn.model_selection import train_test_split
from sklearn.metrics import (
    accuracy_score, precision_score, recall_score, f1_score,
    classification_report, confusion_matrix, mean_absolute_error, mean_squared_error
)
from sklearn.preprocessing import StandardScaler

# ── Rutas ──────────────────────────────────────────────────────────────────────
from pathlib import Path
_HERE   = Path(__file__).parent
_ASSETS = (_HERE / '../Assets').resolve()
CSV_FILES = sorted(_ASSETS.glob('SoccerData_*.csv'))

LOG_DIR  = _HERE / 'logs'
LOG_DIR.mkdir(exist_ok=True)

# ── Parámetros RF ──────────────────────────────────────────────────────────────
N_ESTIMATORS = 200
MAX_DEPTH    = 20     # None = sin límite (puede sobreajustar)
MIN_SAMPLES  = 5
RANDOM_STATE = 42

# ── Features (idénticas a los modelos neurales) ────────────────────────────────
# NOTA: AbsMyPosX/Z y AbsBallPosX/Z NO se incluyen (solo visualización)
FEATURE_COLS = [
    'RelPorteriaRivalX', 'RelPorteriaRivalZ',
    'RelPorteriaPropiaX', 'RelPorteriaPropiaZ',
    'TienePelota',
    'RelPelotaX', 'RelPelotaZ',
    'DistPelota',
    'TienePelotaEquipo',
    'DistPorteriaContraria', 'DistPorteriaPropia',
    'PuntuacionPropia', 'PuntuacionContraria',
    'DistPelotaPorteriaPropia',
    'DistAliadoCercano', 'DistEnemigoCercano',
    'RelAliado1PosX', 'RelAliado1PosZ', 'Aliado1DirX', 'Aliado1DirZ',
    'RelAliado2PosX', 'RelAliado2PosZ', 'Aliado2DirX', 'Aliado2DirZ',
    'RelAliado3PosX', 'RelAliado3PosZ', 'Aliado3DirX', 'Aliado3DirZ',
    'RelEnemigo1PosX', 'RelEnemigo1PosZ', 'Enemigo1DirX', 'Enemigo1DirZ',
    'RelEnemigo2PosX', 'RelEnemigo2PosZ', 'Enemigo2DirX', 'Enemigo2DirZ',
    'RelEnemigo3PosX', 'RelEnemigo3PosZ', 'Enemigo3DirX', 'Enemigo3DirZ',
]
assert len(FEATURE_COLS) == 40

MOVEMENT_COLS = ['InputX', 'InputZ']
ACTION_COLS   = ['Disparo', 'Pase']

# ── Logging simultáneo a fichero ───────────────────────────────────────────────
class Tee:
    def __init__(self, path, encoding='utf-8'):
        self._file = open(path, 'w', encoding=encoding)
        self._stdout = sys.stdout
    def write(self, data):
        self._stdout.write(data)
        self._file.write(data)
    def flush(self):
        self._stdout.flush()
        self._file.flush()
    def __del__(self):
        try:
            self._file.close()
            print(f"[Tee] Log guardado en: {self._file.name}")
        except Exception:
            pass


def load_data():
    """Carga y preprocesa los CSVs de la misma forma que los modelos neurales."""
    frames = []
    for path in CSV_FILES:
        tmp = pd.read_csv(path)
        antes = len(tmp)
        # Filtrar spawn noise
        mask = ((tmp['Aliado1DirX'] == 0) & (tmp['Aliado1DirZ'] == 0) &
                (tmp['Aliado2DirX'] == 0) & (tmp['Aliado2DirZ'] == 0))
        tmp = tmp[~mask]
        print(f"  {path.name}: {antes} → {len(tmp)} filas tras filtro")
        frames.append(tmp)

    raw = pd.concat(frames, ignore_index=True)
    print(f"\n  Total: {len(raw)} filas")

    # Corrección de timing: mover Disparo/Pase al frame previo con TienePelota=1
    LOOKBACK = 5
    for col in ACTION_COLS:
        indices = raw.index[raw[col] == 1].tolist()
        shifted = removed = 0
        for idx in indices:
            if raw.loc[idx, 'TienePelota'] == 1:
                continue
            found = False
            for offset in range(1, LOOKBACK + 1):
                prev = idx - offset
                if prev < 0 or prev not in raw.index:
                    break
                if raw.loc[prev, 'TienePelota'] == 1:
                    raw.loc[idx,  col] = 0
                    raw.loc[prev, col] = 1
                    shifted += 1
                    found = True
                    break
            if not found:
                raw.loc[idx, col] = 0
                removed += 1
        print(f"  Timing fix [{col:7s}]: {shifted} desplazadas, {removed} eliminadas "
              f"→ {int(raw[col].sum())} válidas")

    return raw


def discretize_movement(values, threshold=0.3):
    """Convierte valores continuos a {-1, 0, 1} igual que el Recorder."""
    result = np.zeros_like(values, dtype=int)
    result[values >  threshold] =  1
    result[values < -threshold] = -1
    return result


def print_metrics_block(tag, metrics: dict):
    """Imprime el bloque de métricas en el mismo formato que los modelos neurales."""
    print()
    print("[METRICS_START]")
    print(f"MODEL={tag}")
    for k, v in metrics.items():
        print(f"{k}={v:.4f}")
    print("[METRICS_END]")
    print()


def main():
    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    log_path = LOG_DIR / f"train_rf_baseline_{ts}.txt"
    sys.stdout = Tee(log_path)

    print("=" * 60)
    print("  BASELINE — Random Forest Classifier/Regressor")
    print("=" * 60)
    print(f"\nParámetros RF: n_estimators={N_ESTIMATORS}, max_depth={MAX_DEPTH}")
    print(f"Features: {len(FEATURE_COLS)} columnas\n")

    # ── Cargar datos ────────────────────────────────────────────────────────────
    print("Cargando datos...")
    raw = load_data()

    X = raw[FEATURE_COLS].values
    Y_actions  = raw[ACTION_COLS].values           # [N, 2] binario
    Y_movement = raw[MOVEMENT_COLS].values         # [N, 2] continuo
    Y_mov_disc = np.column_stack([
        discretize_movement(Y_movement[:, 0]),
        discretize_movement(Y_movement[:, 1])
    ])                                             # [N, 2] discreto {-1,0,1}

    # ── Normalización ──────────────────────────────────────────────────────────
    scaler = StandardScaler()
    X_scaled = scaler.fit_transform(X)

    # ── Split ──────────────────────────────────────────────────────────────────
    X_tr, X_te, Ya_tr, Ya_te, Ym_tr, Ym_te, Yd_tr, Yd_te = train_test_split(
        X_scaled, Y_actions, Y_movement, Y_mov_disc,
        test_size=0.2, random_state=RANDOM_STATE
    )
    print(f"\nTrain: {len(X_tr)} | Test: {len(X_te)}")

    all_metrics = {}

    # ══════════════════════════════════════════════════════════════════════════
    # 1. CLASIFICACIÓN DE ACCIONES (Disparo y Pase por separado)
    # ══════════════════════════════════════════════════════════════════════════
    print("\n" + "=" * 60)
    print("  1. CLASIFICACIÓN DE ACCIONES")
    print("=" * 60)

    for i, action in enumerate(ACTION_COLS):
        y_tr = Ya_tr[:, i]
        y_te = Ya_te[:, i]

        pos = y_tr.sum()
        neg = len(y_tr) - pos
        cw  = {0: 1.0, 1: max(1.0, neg / max(pos, 1))}

        clf = RandomForestClassifier(
            n_estimators=N_ESTIMATORS,
            max_depth=MAX_DEPTH,
            min_samples_leaf=MIN_SAMPLES,
            class_weight=cw,
            n_jobs=-1,
            random_state=RANDOM_STATE
        )
        clf.fit(X_tr, y_tr)
        y_pred = clf.predict(X_te)

        acc  = accuracy_score(y_te, y_pred)
        prec = precision_score(y_te, y_pred, zero_division=0)
        rec  = recall_score(y_te, y_pred, zero_division=0)
        f1   = f1_score(y_te, y_pred, zero_division=0)

        print(f"\n  [{action}]")
        print(f"    Positivos en test: {y_te.sum()} / {len(y_te)}")
        print(f"    Accuracy:  {acc*100:.1f}%")
        print(f"    Precision: {prec*100:.1f}%")
        print(f"    Recall:    {rec*100:.1f}%")
        print(f"    F1-Score:  {f1*100:.1f}%")
        print(f"\n{classification_report(y_te, y_pred, zero_division=0)}")

        all_metrics[f"{action}_Accuracy"]  = acc
        all_metrics[f"{action}_Precision"] = prec
        all_metrics[f"{action}_Recall"]    = rec
        all_metrics[f"{action}_F1"]        = f1

    # ══════════════════════════════════════════════════════════════════════════
    # 2. CLASIFICACIÓN DE MOVIMIENTO DISCRETO {-1, 0, 1}
    # ══════════════════════════════════════════════════════════════════════════
    print("\n" + "=" * 60)
    print("  2. CLASIFICACIÓN DE MOVIMIENTO (discreto {-1, 0, 1})")
    print("=" * 60)

    for i, axis in enumerate(['InputX', 'InputZ']):
        y_tr = Yd_tr[:, i]
        y_te = Yd_te[:, i]

        clf = RandomForestClassifier(
            n_estimators=N_ESTIMATORS,
            max_depth=MAX_DEPTH,
            min_samples_leaf=MIN_SAMPLES,
            n_jobs=-1,
            random_state=RANDOM_STATE
        )
        clf.fit(X_tr, y_tr)
        y_pred = clf.predict(X_te)

        acc  = accuracy_score(y_te, y_pred)
        prec = precision_score(y_te, y_pred, average='macro', zero_division=0)
        rec  = recall_score(y_te, y_pred, average='macro', zero_division=0)
        f1   = f1_score(y_te, y_pred, average='macro', zero_division=0)

        print(f"\n  [{axis}]")
        print(f"    Accuracy:          {acc*100:.1f}%")
        print(f"    Precision (macro): {prec*100:.1f}%")
        print(f"    Recall    (macro): {rec*100:.1f}%")
        print(f"    F1-Score  (macro): {f1*100:.1f}%")

        all_metrics[f"{axis}_Accuracy"]  = acc
        all_metrics[f"{axis}_Precision"] = prec
        all_metrics[f"{axis}_Recall"]    = rec
        all_metrics[f"{axis}_F1"]        = f1

    # ══════════════════════════════════════════════════════════════════════════
    # 3. REGRESIÓN DE MOVIMIENTO CONTINUO
    # ══════════════════════════════════════════════════════════════════════════
    print("\n" + "=" * 60)
    print("  3. REGRESIÓN DE MOVIMIENTO (continuo [-1, 1])")
    print("=" * 60)

    reg = RandomForestRegressor(
        n_estimators=N_ESTIMATORS,
        max_depth=MAX_DEPTH,
        min_samples_leaf=MIN_SAMPLES,
        n_jobs=-1,
        random_state=RANDOM_STATE
    )
    reg.fit(X_tr, Ym_tr)
    Ym_pred = reg.predict(X_te)

    for i, axis in enumerate(['InputX', 'InputZ']):
        mae  = mean_absolute_error(Ym_te[:, i], Ym_pred[:, i])
        rmse = np.sqrt(mean_squared_error(Ym_te[:, i], Ym_pred[:, i]))
        print(f"\n  [{axis}]")
        print(f"    MAE:  {mae:.4f}")
        print(f"    RMSE: {rmse:.4f}")
        all_metrics[f"{axis}_MAE"]  = mae
        all_metrics[f"{axis}_RMSE"] = rmse

    # ══════════════════════════════════════════════════════════════════════════
    # 4. BLOQUE DE MÉTRICAS PARA compare_logs.py
    # ══════════════════════════════════════════════════════════════════════════
    # Métricas globales: promedio de las acciones discretas
    actions_f1   = np.mean([all_metrics['Disparo_F1'],  all_metrics['Pase_F1']])
    actions_prec = np.mean([all_metrics['Disparo_Precision'], all_metrics['Pase_Precision']])
    actions_rec  = np.mean([all_metrics['Disparo_Recall'],    all_metrics['Pase_Recall']])
    actions_acc  = np.mean([all_metrics['Disparo_Accuracy'],  all_metrics['Pase_Accuracy']])

    mov_f1   = np.mean([all_metrics['InputX_F1'],        all_metrics['InputZ_F1']])
    mov_acc  = np.mean([all_metrics['InputX_Accuracy'],   all_metrics['InputZ_Accuracy']])

    global_metrics = {
        'Accuracy_Actions':  actions_acc,
        'Precision_Actions': actions_prec,
        'Recall_Actions':    actions_rec,
        'F1_Actions':        actions_f1,
        'Accuracy_Movement': mov_acc,
        'F1_Movement':       mov_f1,
        'MAE_InputX':        all_metrics['InputX_MAE'],
        'MAE_InputZ':        all_metrics['InputZ_MAE'],
    }

    print_metrics_block("RandomForest_Baseline", global_metrics)

    # ══════════════════════════════════════════════════════════════════════════
    # 5. TABLA RESUMEN PARA TFG
    # ══════════════════════════════════════════════════════════════════════════
    print("=" * 60)
    print("  TABLA RESUMEN — RANDOM FOREST BASELINE")
    print("=" * 60)
    print(f"{'Métrica':<30} {'Valor':>10}")
    print("-" * 42)
    for k, v in global_metrics.items():
        unit = "%" if "Accuracy" in k or "Precision" in k or "Recall" in k or "F1" in k else ""
        val  = f"{v*100:.2f}%" if unit == "%" else f"{v:.4f}"
        print(f"  {k:<28} {val:>10}")
    print("=" * 60)
    print(f"\nLog guardado en: {log_path}")


if __name__ == '__main__':
    main()
