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
import os
import json
import glob

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
STATE_LOSS_WEIGHT    = 2.0
MOVEMENT_LOSS_WEIGHT = 1.0

# Nombres de los estados
STATE_NAMES = ['Defendiendo', 'Atacando', 'Pasando', 'Tirando']
NUM_STATES  = len(STATE_NAMES)

# Definicion de columnas (Mismo orden que Unity AIController / Recorder)
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
MOVEMENT_COLS = ['InputX', 'InputZ']


# ============================================================================
# 2. DERIVACION DEL ESTADO
# ============================================================================
def derive_state(row):
    """
    Regla heurística para derivar el estado a partir de las columnas existentes.
    Prioridad: Tirando > Pasando > Atacando > Defendiendo
    """
    if row['Disparo'] == 1:
        return 3  # Tirando
    elif row['Pase'] == 1:
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

        # ── BALANCEO HÍBRIDO: oversampling moderado + class weights suaves ──
        state_counts = raw['Estado'].value_counts().sort_index()
        print(f"  Distribución de estados ORIGINAL:")
        for s_id, s_name in enumerate(STATE_NAMES):
            count = state_counts.get(s_id, 0)
            print(f"    {s_name} ({s_id}): {count}")

        # Oversampling moderado: las clases minoritarias se llevan a la MEDIANA
        # (no al máximo), así no se duplican 100x
        sorted_counts = sorted(state_counts.values)
        oversample_target = int(sorted_counts[len(sorted_counts) // 2])  # mediana
        oversample_target = max(oversample_target, 3000)  # mínimo 3000
        print(f"  Target de oversampling (mediana capped): {oversample_target}")

        balanced = []
        for state_val, group in raw.groupby('Estado'):
            if len(group) < oversample_target:
                group = group.sample(n=oversample_target, replace=True, random_state=42)
            balanced.append(group)

        df_bal = pd.concat(balanced).reset_index(drop=True)
        self.data = df_bal.sample(frac=1, random_state=42).reset_index(drop=True)

        state_counts_after = self.data['Estado'].value_counts().sort_index()
        print(f"  Distribución de estados TRAS oversampling moderado:")
        for s_id, s_name in enumerate(STATE_NAMES):
            count = state_counts_after.get(s_id, 0)
            print(f"    {s_name} ({s_id}): {count}")

        # Class weights SUAVES (raíz cuadrada del ratio inverso)
        # Esto compensa el desbalance restante sin desestabilizar gradientes
        total = len(self.data)
        self.class_weights = torch.zeros(NUM_STATES, dtype=torch.float32)
        for s_id in range(NUM_STATES):
            count = state_counts_after.get(s_id, 1)
            raw_weight = total / (NUM_STATES * count)
            # Raíz cuadrada para suavizar: 45x → ~6.7x, 1x → 1x
            self.class_weights[s_id] = float(np.sqrt(raw_weight))

        print(f"  Pesos de clase para CrossEntropyLoss (sqrt-dampened):")
        for s_id, s_name in enumerate(STATE_NAMES):
            print(f"    {s_name}: {self.class_weights[s_id]:.2f}")

        # Tensores
        self.X  = torch.tensor(self.data[FEATURE_COLS].values, dtype=torch.float32)
        self.Ys = torch.tensor(self.data['Estado'].values,     dtype=torch.long)
        self.Ym = torch.tensor(self.data[MOVEMENT_COLS].values, dtype=torch.float32)

        # Normalización global
        self.X = (self.X - global_mean) / global_std

    def __len__(self):
        return len(self.data)

    def __getitem__(self, idx):
        return self.X[idx], self.Ys[idx], self.Ym[idx]


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
def train_fsm():
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

    # Entrenamiento
    print(f"\nEntrenando {EPOCHS} epochs...")
    for epoch in range(EPOCHS):
        model.train()
        total_loss = total_ce = total_mse = 0.0

        for batch_X, batch_Ys, batch_Ym in train_loader:
            optimizer.zero_grad()

            pred_state, pred_mov = model(batch_X)

            loss_ce  = ce_crit(pred_state, batch_Ys)
            loss_mse = mse_crit(pred_mov, batch_Ym)
            loss     = STATE_LOSS_WEIGHT * loss_ce + MOVEMENT_LOSS_WEIGHT * loss_mse

            loss.backward()
            optimizer.step()

            total_loss += loss.item()
            total_ce   += loss_ce.item()
            total_mse  += loss_mse.item()

        if (epoch + 1) % 30 == 0:
            n = len(train_loader)
            print(f"  Epoch [{epoch+1:3d}/{EPOCHS}] "
                  f"Loss={total_loss/n:.4f}  CE={total_ce/n:.4f}  MSE={total_mse/n:.4f}")

    # ── EVALUACIÓN ──
    print("\n" + "=" * 60)
    print("  EVALUACIÓN")
    print("=" * 60)
    model.eval()

    all_pred_state, all_true_state = [], []
    all_pred_mov, all_true_mov = [], []

    with torch.no_grad():
        for batch_X, batch_Ys, batch_Ym in test_loader:
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
    acc_mov = accuracy_score(combined_true, combined_pred)
    print(f"  Accuracy Movimiento Combinado: {acc_mov*100:.1f}%")

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
    train_fsm()
