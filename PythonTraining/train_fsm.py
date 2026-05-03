import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
import pandas as pd
import numpy as np
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader, Subset
from sklearn.model_selection import train_test_split
from sklearn.metrics import (accuracy_score, classification_report,
                             precision_score, recall_score, f1_score,
                             confusion_matrix)
from imblearn.over_sampling import SMOTE
import os
import json
import glob
import datetime

# ============================================================================
# TEE — escribe en consola Y en fichero de log simultaneamente
# ============================================================================
class Tee:
    """Duplica stdout a un fichero de log y a la consola al mismo tiempo."""
    def __init__(self, filepath):
        os.makedirs(os.path.dirname(filepath), exist_ok=True)
        self._file   = open(filepath, 'w', encoding='utf-8')
        self._stdout = sys.stdout
    def write(self, data):
        self._stdout.write(data)
        self._file.write(data)
    def flush(self):
        self._stdout.flush()
        self._file.flush()
    def close(self):
        sys.stdout = self._stdout
        self._file.close()
        print(f"[Tee] Log guardado en: {self._file.name}")

# ============================================================================
# 1. PARAMETROS
# ============================================================================
CSV_FILES = glob.glob('../Assets/SoccerData_*.csv')

ONNX_OUTPUT   = '../Assets/SoccerModel_FSM.onnx'
SCALER_OUTPUT = '../Assets/scaler_fsm.json'
EPOCHS        = 150
BATCH_SIZE    = 32
LEARNING_RATE = 0.001

# Pesos relativos de cada pérdida
STATE_LOSS_WEIGHT     = 2.0
MOVEMENT_LOSS_WEIGHT  = 1.0
# Penalización por predecir Tirando/Pasando sin tener la pelota.
NO_BALL_PENALTY_WEIGHT = 0.5

# Máximo de muestras sintéticas por clase minoritaria (SMOTE cap).
# Sube este número si el modelo ignora Tirando/Pasando.
# Bájalo si dispara/pasa demasiado agresivamente.
# Regla general: 5-15% del tamaño de la clase mayoritaria.
SMOTE_MINORITY_CAP = 600

# Nombres de los estados
STATE_NAMES = ['Defendiendo', 'Atacando', 'Pasando', 'Tirando']
NUM_STATES  = len(STATE_NAMES)

# Definicion de columnas (Mismo orden que Unity AIController / Recorder)
# NOTA: AbsMyPosX, AbsMyPosZ, AbsBallPosX, AbsBallPosZ NO están aquí.
# Son columnas de visualización (heatmap) — nunca se usan como features.
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
]  # 40 features exactas
assert len(FEATURE_COLS) == 40, f"Se esperaban 40 features, hay {len(FEATURE_COLS)}"
MOVEMENT_COLS = ['InputX', 'InputZ']


# ============================================================================
# 2. DERIVACION DEL ESTADO
# ============================================================================
def derive_state(row):
    """
    Regla heurística para derivar el estado a partir de las columnas existentes.
    Prioridad: Tirando > Pasando > Atacando > Defendiendo

    IMPORTANTE: Tirando y Pasando sólo se asignan cuando TienePelota==1.
    Si el frame tiene Disparo=1 pero TienePelota=0 es un artefacto de timing
    (la pelota ya salió) y debe clasificarse como Atacando o Defendiendo.
    """
    has_ball = row['TienePelota'] == 1

    if row['Disparo'] == 1 and has_ball:
        return 3  # Tirando
    elif row['Pase'] == 1 and has_ball:
        return 2  # Pasando
    elif row['TienePelotaEquipo'] == 1:
        return 1  # Atacando
    else:
        return 0  # Defendiendo


# ============================================================================
# 3. MODELO FSM (dos cabezas: clasificacion de estado + movimiento)
# ============================================================================
class SoccerFSMModel(nn.Module):
    def __init__(self, input_size: int = 40, num_states: int = 4):
        super().__init__()
        self.backbone = nn.Sequential(
            nn.Linear(input_size, 128),
            nn.ReLU(),
            nn.Dropout(0.2),
            nn.Linear(128, 64),
            nn.ReLU(),
        )
        # Cabeza de clasificación de estado (4 clases)
        # No se aplica softmax aquí porque CrossEntropyLoss lo hace internamente
        self.state_head = nn.Sequential(
            nn.Linear(64, num_states),
        )
        # Cabeza de movimiento (InputX, InputZ en rango [-1, 1])
        self.movement_head = nn.Sequential(
            nn.Linear(64, 2),
            nn.Tanh(),
        )

    def forward(self, x):
        shared   = self.backbone(x)
        state    = self.state_head(shared)
        movement = self.movement_head(shared)
        return state, movement


# ============================================================================
# 4. DATASET
# ============================================================================
class SoccerFSMDataset(Dataset):
    def __init__(self, df: pd.DataFrame, global_mean: torch.Tensor, global_std: torch.Tensor):
        if len(df) == 0:
            raise ValueError("El DataFrame esta vacio.")

        raw = df.copy()

        # ── Derivar la columna Estado ──
        raw['Estado'] = raw.apply(derive_state, axis=1)

        # ── SMOTE MULTICLASE para los 4 estados FSM ──
        # Genera muestras sintéticas interpolando entre vecinos reales
        # de cada clase minoritaria en el espacio de features.
        state_counts = raw['Estado'].value_counts().sort_index()
        print(f"  Distribución de estados ANTES de SMOTE:")
        for s_id, s_name in enumerate(STATE_NAMES):
            print(f"    {s_name} ({s_id}): {state_counts.get(s_id, 0)}")

        smote_cols = FEATURE_COLS + MOVEMENT_COLS

        # k_neighbors no puede superar el nº de muestras de la clase más pequeña - 1
        min_state_count = min(
            state_counts.get(s, 0) for s in range(NUM_STATES) if state_counts.get(s, 0) > 0
        )
        k = max(min(5, min_state_count - 1), 1)

        # Objetivo: SMOTE_MINORITY_CAP muestras para clases que estén por debajo.
        # Se usa la mediana como referencia para las clases medias, pero se limita
        # el cap a SMOTE_MINORITY_CAP para las clases muy minoritarias.
        sorted_counts = sorted(state_counts.values)
        median_count  = int(sorted_counts[len(sorted_counts) // 2])

        sampling_strategy = {}
        for s_id in range(NUM_STATES):
            current = state_counts.get(s_id, 0)
            if current == 0:
                continue  # Sin muestras reales, SMOTE no puede generar
            # Las clases muy pequeñas se limitan al cap;
            # las clases medianas llegan a la mediana normalmente.
            target = min(median_count, SMOTE_MINORITY_CAP) if current < SMOTE_MINORITY_CAP \
                     else median_count
            if current < target:
                sampling_strategy[s_id] = target

        print(f"  SMOTE targets (cap={SMOTE_MINORITY_CAP}):")
        for s_id, s_name in enumerate(STATE_NAMES):
            current = state_counts.get(s_id, 0)
            tgt = sampling_strategy.get(s_id, current)
            print(f"    {s_name}: {current} → {tgt}")

        if sampling_strategy:
            X_sm = raw[smote_cols].values
            y_sm = raw['Estado'].values

            smote = SMOTE(sampling_strategy=sampling_strategy, k_neighbors=k, random_state=42)
            X_res, y_res = smote.fit_resample(X_sm, y_sm)

            df_res = pd.DataFrame(X_res, columns=smote_cols)
            # Discretizar InputX/Z sintéticos al valor más cercano {-1, 0, 1}
            for col in MOVEMENT_COLS:
                df_res[col] = df_res[col].apply(
                    lambda v: 1.0 if v > 0.5 else (-1.0 if v < -0.5 else 0.0)
                )
            df_res['Estado'] = y_res.astype(int)
            raw = df_res
        # Si todos los estados ya alcanzan el target, no hace falta SMOTE

        state_counts_after = raw['Estado'].value_counts().sort_index()
        print(f"  Distribución de estados TRAS SMOTE (k={k}, cap={SMOTE_MINORITY_CAP}):")
        for s_id, s_name in enumerate(STATE_NAMES):
            print(f"    {s_name} ({s_id}): {state_counts_after.get(s_id, 0)}")

        # ── Class weights SUAVES (sqrt-dampened) para el desbalance residual ──
        total = len(raw)
        self.class_weights = torch.zeros(NUM_STATES, dtype=torch.float32)
        for s_id in range(NUM_STATES):
            count = state_counts_after.get(s_id, 1)
            raw_weight = total / (NUM_STATES * count)
            self.class_weights[s_id] = float(np.sqrt(raw_weight))

        print(f"  Pesos de clase (sqrt-dampened):")
        for s_id, s_name in enumerate(STATE_NAMES):
            print(f"    {s_name}: {self.class_weights[s_id]:.3f}")

        # Tensores principales
        self.data = raw.sample(frac=1, random_state=42).reset_index(drop=True)
        self.X       = torch.tensor(self.data[FEATURE_COLS].values,   dtype=torch.float32)
        self.Ys      = torch.tensor(self.data['Estado'].values,        dtype=torch.long)
        self.Ym      = torch.tensor(self.data[MOVEMENT_COLS].values,   dtype=torch.float32)
        # TienePelota (col 4 de FEATURE_COLS) guardado por separado para la penalty
        self.has_ball = torch.tensor(self.data['TienePelota'].values,  dtype=torch.float32)

        # Normalización global
        self.X = (self.X - global_mean) / global_std

    def __len__(self):
        return len(self.data)

    def __getitem__(self, idx):
        return self.X[idx], self.Ys[idx], self.Ym[idx], self.has_ball[idx]


# ============================================================================
# 5. HELPERS
# ============================================================================
def discretize(values, threshold=0.3):
    result = np.zeros_like(values, dtype=int)
    result[values >  threshold] =  1
    result[values < -threshold] = -1
    return result


# ============================================================================
# 6. CARGA DE DATOS
# ============================================================================
def load_data(csv_files):
    frames = []
    for path in csv_files:
        if not os.path.exists(path):
            print(f"  AVISO: no se encuentra {path} -- omitido")
            continue
        tmp = pd.read_csv(path)

        # ── FILTRO SPAWN NOISE ──
        antes = len(tmp)
        spawn_mask = (
            (tmp['Aliado1DirX'] == 0) & (tmp['Aliado1DirZ'] == 0) &
            (tmp['Aliado2DirX'] == 0) & (tmp['Aliado2DirZ'] == 0)
        )
        tmp = tmp[~spawn_mask]
        despues = len(tmp)
        print(f"  {os.path.basename(path)}: {antes} -> {despues} tras filtro")
        frames.append(tmp)

    if not frames:
        raise FileNotFoundError("Ningun CSV valido encontrado.")

    raw = pd.concat(frames, ignore_index=True)
    print(f"\nTOTAL combinado: {len(raw)} filas")
    print(f"  Disparo=1 en total: {int(raw['Disparo'].sum())}")
    print(f"  Pase=1    en total: {int(raw['Pase'].sum())}")

    # ── CORRECCIÓN DE TIMING ───────────────────────────────────────────────
    # El Recorder graba Disparo/Pase en el frame donde la pelota ya salió
    # (TienePelota=0). derive_state() necesita TienePelota=1 para asignar
    # el estado correcto, así que desplazamos la etiqueta al frame anterior
    # más cercano donde TienePelota=1.
    LOOKBACK = 5
    for action_col in ['Disparo', 'Pase']:
        action_indices = raw.index[raw[action_col] == 1].tolist()
        shifted = 0
        removed = 0
        for idx in action_indices:
            if raw.loc[idx, 'TienePelota'] == 1:
                continue   # ya está en el frame correcto
            found = False
            for offset in range(1, LOOKBACK + 1):
                prev_idx = idx - offset
                if prev_idx < 0 or prev_idx not in raw.index:
                    break
                if raw.loc[prev_idx, 'TienePelota'] == 1:
                    raw.loc[idx,      action_col] = 0
                    raw.loc[prev_idx, action_col] = 1
                    shifted += 1
                    found = True
                    break
            if not found:
                # Sin frame previo con balón → ruido, eliminar la etiqueta
                raw.loc[idx, action_col] = 0
                removed += 1
        valid = int(raw[action_col].sum())
        print(f"  Timing fix [{action_col:7s}]: {shifted} desplazadas, "
              f"{removed} eliminadas → {valid} válidas")

    # Calcular scaler global
    X_raw = torch.tensor(raw[FEATURE_COLS].values, dtype=torch.float32)
    global_mean = X_raw.mean(dim=0, keepdim=True)
    global_std  = X_raw.std(dim=0, keepdim=True)
    global_std[global_std == 0] = 1.0

    # Guardar scaler para Unity
    with open(SCALER_OUTPUT, "w") as f:
        json.dump({
            "mean": global_mean.squeeze().tolist(),
            "std":  global_std.squeeze().tolist()
        }, f)
    print(f"Scaler FSM guardado en {SCALER_OUTPUT}")

    return raw, global_mean, global_std


# ============================================================================
# 7. ENTRENAMIENTO
# ============================================================================
def train_fsm(timestamp=''):
    print("=" * 60)
    print("  ENTRENANDO MODELO FSM (Máquina de Estados)")
    print("=" * 60)

    # Cargar datos
    print("\nCargando datos...")
    raw, global_mean, global_std = load_data(CSV_FILES)

    # Crear dataset
    print("\nCreando dataset con balanceo...")
    dataset = SoccerFSMDataset(raw, global_mean, global_std)
    print(f"Filas tras balanceo: {len(dataset)}")

    # Split train/test
    indices = np.arange(len(dataset))
    train_idx, test_idx = train_test_split(indices, test_size=0.2, random_state=42)

    if len(train_idx) == 0 or len(test_idx) == 0:
        print("ERROR: No hay datos suficientes tras el split.")
        return

    train_loader = DataLoader(Subset(dataset, train_idx), batch_size=BATCH_SIZE, shuffle=True)
    test_loader  = DataLoader(Subset(dataset, test_idx),  batch_size=BATCH_SIZE, shuffle=False)

    # Modelo
    INPUT_SIZE = len(FEATURE_COLS)
    model      = SoccerFSMModel(INPUT_SIZE, NUM_STATES)
    ce_crit    = nn.CrossEntropyLoss(weight=dataset.class_weights)
    mse_crit   = nn.MSELoss()
    optimizer  = optim.Adam(model.parameters(), lr=LEARNING_RATE)

    # Índices de los estados que requieren posesión de balón
    IDX_PASANDO = 2
    IDX_TIRANDO = 3

    # Entrenamiento
    print(f"\nEntrenando {EPOCHS} epochs...")
    print(f"  Penalización sin balón (NO_BALL_PENALTY_WEIGHT): {NO_BALL_PENALTY_WEIGHT}")
    for epoch in range(EPOCHS):
        model.train()
        total_loss = total_ce = total_mse = total_pen = 0.0

        for batch_X, batch_Ys, batch_Ym, batch_has_ball in train_loader:
            optimizer.zero_grad()

            pred_state, pred_mov = model(batch_X)

            # Pérdida principal
            loss_ce  = ce_crit(pred_state, batch_Ys)
            loss_mse = mse_crit(pred_mov, batch_Ym)

            # Penalización: si no tiene balón, el logit de Tirando y Pasando
            # debe ser negativo (el modelo aprende a suprimirlos por sí solo).
            # Se usa clamp(min=0) para penalizar solo logits positivos.
            no_ball = (batch_has_ball == 0).float()  # 1 donde NO hay balón
            pen_tirando = (pred_state[:, IDX_TIRANDO] * no_ball).clamp(min=0).mean()
            pen_pasando = (pred_state[:, IDX_PASANDO] * no_ball).clamp(min=0).mean()
            loss_penalty = pen_tirando + pen_pasando

            loss = (STATE_LOSS_WEIGHT    * loss_ce  +
                    MOVEMENT_LOSS_WEIGHT  * loss_mse +
                    NO_BALL_PENALTY_WEIGHT * loss_penalty)

            loss.backward()
            optimizer.step()

            total_loss += loss.item()
            total_ce   += loss_ce.item()
            total_mse  += loss_mse.item()
            total_pen  += loss_penalty.item()

        if (epoch + 1) % 30 == 0:
            n = len(train_loader)
            print(f"  Epoch [{epoch+1:3d}/{EPOCHS}] "
                  f"Loss={total_loss/n:.4f}  CE={total_ce/n:.4f}  "
                  f"MSE={total_mse/n:.4f}  Penalty={total_pen/n:.4f}")

    n_batches = len(train_loader)
    last_loss = total_loss / n_batches if n_batches > 0 else 0.0

    # ── EVALUACIÓN ──
    print("\n" + "=" * 60)
    print("  EVALUACIÓN")
    print("=" * 60)
    model.eval()

    all_pred_state, all_true_state = [], []
    all_pred_mov, all_true_mov = [], []

    with torch.no_grad():
        for batch_X, batch_Ys, batch_Ym, _ in test_loader:
            pred_state, pred_mov = model(batch_X)

            # Estado: argmax de logits
            pred_labels = torch.argmax(pred_state, dim=1).cpu().numpy()
            all_pred_state.extend(pred_labels)
            all_true_state.extend(batch_Ys.cpu().numpy())

            all_pred_mov.append(pred_mov.cpu().numpy())
            all_true_mov.append(batch_Ym.cpu().numpy())

    # Métricas de estado
    acc_state = accuracy_score(all_true_state, all_pred_state)
    prec_macro = precision_score(all_true_state, all_pred_state, average='macro', zero_division=0)
    rec_macro  = recall_score(all_true_state, all_pred_state, average='macro', zero_division=0)
    f1_macro   = f1_score(all_true_state, all_pred_state, average='macro', zero_division=0)
    prec_weighted = precision_score(all_true_state, all_pred_state, average='weighted', zero_division=0)
    rec_weighted  = recall_score(all_true_state, all_pred_state, average='weighted', zero_division=0)
    f1_weighted   = f1_score(all_true_state, all_pred_state, average='weighted', zero_division=0)

    print(f"\n  ┌─────────────────────────────────────────────┐")
    print(f"  │        MÉTRICAS GLOBALES DE ESTADO          │")
    print(f"  ├─────────────────────────────────────────────┤")
    print(f"  │  Accuracy:            {acc_state*100:6.1f}%              │")
    print(f"  │  Precision (macro):    {prec_macro*100:6.1f}%              │")
    print(f"  │  Recall    (macro):    {rec_macro*100:6.1f}%              │")
    print(f"  │  F1-Score  (macro):    {f1_macro*100:6.1f}%              │")
    print(f"  │  Precision (weighted): {prec_weighted*100:6.1f}%              │")
    print(f"  │  Recall    (weighted): {rec_weighted*100:6.1f}%              │")
    print(f"  │  F1-Score  (weighted): {f1_weighted*100:6.1f}%              │")
    print(f"  └─────────────────────────────────────────────┘")

    print("\n  Classification Report (por clase):")
    print(classification_report(
        all_true_state, all_pred_state,
        target_names=STATE_NAMES, zero_division=0
    ))

    # Matriz de confusión
    cm = confusion_matrix(all_true_state, all_pred_state)
    print("  Matriz de Confusión:")
    print(f"  {'':>15s}  {'Pred Def':>8s} {'Pred Ata':>8s} {'Pred Pas':>8s} {'Pred Tir':>8s}")
    for i, name in enumerate(STATE_NAMES):
        row = '  '.join(f'{v:8d}' for v in cm[i])
        print(f"  {('Real '+name):>15s}  {row}")

    # Métricas de movimiento
    pred_mov = np.vstack(all_pred_mov)
    true_mov = np.vstack(all_true_mov)
    pred_X = discretize(pred_mov[:, 0])
    true_X = discretize(true_mov[:, 0])
    pred_Z = discretize(pred_mov[:, 1])
    true_Z = discretize(true_mov[:, 1])
    combined_pred = [f"{x},{z}" for x, z in zip(pred_X, pred_Z)]
    combined_true = [f"{x},{z}" for x, z in zip(true_X, true_Z)]
    acc_mov  = accuracy_score(combined_true, combined_pred)
    prec_mov = precision_score(combined_true, combined_pred, average='macro', zero_division=0)
    rec_mov  = recall_score(combined_true,   combined_pred, average='macro', zero_division=0)
    f1_mov   = f1_score(combined_true,       combined_pred, average='macro', zero_division=0)
    print(f"  Movimiento Combinado: Acc={acc_mov*100:.1f}%  Prec={prec_mov*100:.1f}%  Rec={rec_mov*100:.1f}%  F1={f1_mov*100:.1f}%")

    print("[METRICS_START]")
    print(f"MODEL=FSM")
    print(f"TIMESTAMP={timestamp}")
    print(f"ACC_STATE={acc_state*100:.2f}")
    print(f"PREC_STATE={prec_macro*100:.2f}")
    print(f"REC_STATE={rec_macro*100:.2f}")
    print(f"F1_STATE={f1_macro*100:.2f}")
    print(f"PREC_STATE_W={prec_weighted*100:.2f}")
    print(f"REC_STATE_W={rec_weighted*100:.2f}")
    print(f"F1_STATE_W={f1_weighted*100:.2f}")
    print(f"ACC_MOV={acc_mov*100:.2f}")
    print(f"PREC_MOV={prec_mov*100:.2f}")
    print(f"REC_MOV={rec_mov*100:.2f}")
    print(f"F1_MOV={f1_mov*100:.2f}")
    print(f"LOSS_FINAL={last_loss:.4f}")
    print("[METRICS_END]")

    # ── EXPORTAR ONNX ──
    print("\n" + "=" * 60)
    print("  EXPORTACIÓN ONNX")
    print("=" * 60)
    dummy = torch.randn(1, INPUT_SIZE)
    try:
        torch.onnx.export(
            model, dummy, ONNX_OUTPUT,
            export_params=True, opset_version=18,
            do_constant_folding=True,
            input_names=['vector_observation'],
            output_names=['state_logits', 'continuous_actions'],
            dynamic_axes={
                'vector_observation': {0: 'batch_size'},
                'state_logits':      {0: 'batch_size'},
                'continuous_actions': {0: 'batch_size'},
            }
        )
        print(f"  Modelo FSM guardado en {ONNX_OUTPUT}")
    except Exception as e:
        print(f"  ONNX export falló: {e}")

    print("\n¡Entrenamiento FSM completado!")


if __name__ == "__main__":
    ts  = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
    log_dir  = os.path.join(os.path.dirname(__file__) or '.', 'logs')
    log_path = os.path.join(log_dir, f'train_fsm_{ts}.txt')
    tee = Tee(log_path)
    sys.stdout = tee
    try:
        train_fsm(timestamp=ts)
    finally:
        tee.close()
