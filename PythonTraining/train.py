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
from sklearn.metrics import (accuracy_score, precision_score, recall_score,
                             f1_score, classification_report)
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

# 1. PARAMETERS
CSV_FILES = glob.glob('../Assets/SoccerData_*.csv')

# Prefijo para guardar los 4 modelos
ONNX_OUTPUT_PREFIX = '../Assets/SoccerModel_'
EPOCHS        = 150
BATCH_SIZE    = 32
LEARNING_RATE = 0.001

# Peso relativo de cada perdida
MOVEMENT_LOSS_WEIGHT = 1.0
ACTION_LOSS_WEIGHT   = 2.0

# Definicion de columnas (Mismo orden que Unity AIController)
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
ACTION_COLS   = ['Disparo', 'Pase']


# 2. MODELO
class SoccerAgentModel(nn.Module):
    def __init__(self, input_size: int = 40):
        super().__init__()
        self.backbone = nn.Sequential(
            nn.Linear(input_size, 128),
            nn.ReLU(),
            nn.Dropout(0.2),
            nn.Linear(128, 64),
            nn.ReLU(),
        )
        self.movement_head = nn.Sequential(
            nn.Linear(64, 2),
            nn.Tanh()
        )
        # Sin Sigmoid: BCEWithLogitsLoss lo aplica internamente (mas estable)
        self.action_head = nn.Sequential(
            nn.Linear(64, 2),
        )

    def forward(self, x):
        shared   = self.backbone(x)
        movement = self.movement_head(shared)
        actions  = self.action_head(shared)   # logits, sin Sigmoid
        return movement, actions


# 3. DATASET
class SoccerDataset(Dataset):
    def __init__(self, df: pd.DataFrame, global_mean: torch.Tensor, global_std: torch.Tensor):
        if len(df) == 0:
            raise ValueError("El DataFrame esta vacio.")

        raw = df.copy()

        # ── CORRECCIÓN DE TIMING: propagar etiqueta al frame con TienePelota=1 ──
        LOOKBACK = 5
        shifted_shoot = shifted_pass = 0
        for action_col in ['Disparo', 'Pase']:
            for idx in raw.index[raw[action_col] == 1].tolist():
                if raw.loc[idx, 'TienePelota'] == 1:
                    continue
                found = False
                for offset in range(1, LOOKBACK + 1):
                    prev_idx = idx - offset
                    if prev_idx < 0:
                        break
                    if prev_idx in raw.index and raw.loc[prev_idx, 'TienePelota'] == 1:
                        raw.loc[idx, action_col]      = 0
                        raw.loc[prev_idx, action_col] = 1
                        if action_col == 'Disparo': shifted_shoot += 1
                        else:                       shifted_pass  += 1
                        found = True
                        break
                if not found:
                    raw.loc[idx, action_col] = 0  # ruido
        print(f"  Timing-shift: Disparo={shifted_shoot}, Pase={shifted_pass} etiquetas movidas")

        # ── SMOTE: Synthetic Minority Oversampling Technique ──
        # Clase combinada: 0=sin acción, 1=Disparo, 2=Pase
        raw['_smote_class'] = 0
        raw.loc[raw['Pase']    == 1, '_smote_class'] = 2
        raw.loc[raw['Disparo'] == 1, '_smote_class'] = 1

        smote_cols   = FEATURE_COLS + MOVEMENT_COLS
        class_counts = raw['_smote_class'].value_counts().sort_index()
        print(f"  Clases antes de SMOTE: {dict(class_counts)}")

        n_minority = sum(class_counts.get(c, 0) for c in [1, 2])
        if n_minority > 0:
            X_sm = raw[smote_cols].values
            y_sm = raw['_smote_class'].values

            min_count = min(class_counts.get(c, 0) for c in [1, 2] if class_counts.get(c, 0) > 0)
            k = max(min(5, min_count - 1), 1)

            majority_n = class_counts.get(0, 1)
            target     = max(int(majority_n * 0.15), min_count)

            sampling_strategy = {}
            for c in [1, 2]:
                if class_counts.get(c, 0) > 0:
                    sampling_strategy[c] = max(target, class_counts.get(c, 0))

            smote = SMOTE(sampling_strategy=sampling_strategy, k_neighbors=k, random_state=42)
            X_res, y_res = smote.fit_resample(X_sm, y_sm)

            df_res = pd.DataFrame(X_res, columns=smote_cols)
            for col in MOVEMENT_COLS:
                df_res[col] = df_res[col].apply(
                    lambda v: 1.0 if v > 0.5 else (-1.0 if v < -0.5 else 0.0)
                )
            df_res['Disparo'] = (y_res == 1).astype(int)
            df_res['Pase']    = (y_res == 2).astype(int)
            raw = df_res
            print(f"  SMOTE aplicado (k={k}): {len(raw)} filas | "
                  f"Disparo={int(raw['Disparo'].sum())} | Pase={int(raw['Pase'].sum())}")
        else:
            print("  WARNING: Sin eventos Disparo/Pase — SMOTE omitido.")

        if '_smote_class' in raw.columns:
            raw.drop('_smote_class', axis=1, inplace=True)

        # ── BALANCEO DE MOVIMIENTO (oversampling a la clase max) ──
        raw['_mov'] = raw['InputX'].astype(str) + '_' + raw['InputZ'].astype(str)
        mov_max = int(raw['_mov'].value_counts().max())
        balanced = []
        for _, group in raw.groupby('_mov'):
            if len(group) < mov_max:
                group = group.sample(n=mov_max, replace=True, random_state=42)
            balanced.append(group)
        raw = pd.concat(balanced).reset_index(drop=True)
        if '_mov' in raw.columns:
            raw.drop('_mov', axis=1, inplace=True)

        self.data = raw.sample(frac=1, random_state=42).reset_index(drop=True)
        print(f"  Filas tras balanceo movimiento: {len(self.data)}")

        self.X  = torch.tensor(self.data[FEATURE_COLS].values,  dtype=torch.float32)
        self.Ym = torch.tensor(self.data[MOVEMENT_COLS].values, dtype=torch.float32)
        self.Ya = torch.tensor(self.data[ACTION_COLS].values,   dtype=torch.float32)

        # Normalizacion global
        self.X = (self.X - global_mean) / global_std

    def __len__(self):
        return len(self.data)

    def __getitem__(self, idx):
        return self.X[idx], self.Ym[idx], self.Ya[idx]


# 4. HELPERS
def discretize(values, threshold=0.3):
    result = np.zeros_like(values, dtype=int)
    result[values >  threshold] =  1
    result[values < -threshold] = -1
    return result

def binarize(values, threshold=0.5):
    return (values >= threshold).astype(int)


# 5. CARGA Y PARTICION DE DATOS
def load_and_split_data(csv_files):
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

    # Guardar scaler global para Unity
    scaler_path = os.path.join(os.path.dirname(ONNX_OUTPUT_PREFIX), "scaler.json")
    with open(scaler_path, "w") as f:
        json.dump({"mean": global_mean.squeeze().tolist(),
                   "std":  global_std.squeeze().tolist()}, f)
    print(f"Scaler GLOBAL guardado en {scaler_path}")

    # ── HEURISTICAS DE DIVISION ──
    # 1. Recover: El equipo no tiene el balon, y no se esta disparando ni pasando
    df_recover = raw[(raw['TienePelotaEquipo'] != 1) & (raw['Disparo'] == 0) & (raw['Pase'] == 0)].copy()
    
    # 2. Approach: Nuestro equipo tiene la pelota (1), pero no estamos disparando ni pasando
    df_approach = raw[(raw['TienePelotaEquipo'] == 1) & (raw['Disparo'] == 0) & (raw['Pase'] == 0)].copy()
    
    # 3. Pass: Seleccionamos los frames donde Pase == 1
    df_pass = raw[raw['Pase'] == 1].copy()
    
    # 4. Shoot: Seleccionamos los frames donde Disparo == 1
    df_shoot = raw[raw['Disparo'] == 1].copy()

    datasets = {
        'Recover': df_recover,
        'Approach': df_approach,
        'Pass': df_pass,
        'Shoot': df_shoot
    }
    
    return datasets, global_mean, global_std


# 6. ENTRENAMIENTO Y EXPORTACION
def train_model(model_name, df, global_mean, global_std):
    print(f"\n" + "="*60)
    print(f"  ENTRENANDO MODELO: {model_name.upper()} (Filas crudas: {len(df)})")
    print("="*60)
    
    if len(df) == 0:
        print(f"OMITIENDO {model_name}: No hay datos suficientes.")
        return

    dataset = SoccerDataset(df, global_mean, global_std)
    print(f"Filas tras balanceo: {len(dataset)}")

    indices = np.arange(len(dataset))
    train_idx, test_idx = train_test_split(indices, test_size=0.2, random_state=42)
    
    if len(train_idx) == 0 or len(test_idx) == 0:
        print(f"OMITIENDO {model_name}: No hay datos suficientes tras el split.")
        return
        
    train_loader = DataLoader(Subset(dataset, train_idx), batch_size=BATCH_SIZE, shuffle=True)
    test_loader  = DataLoader(Subset(dataset, test_idx),  batch_size=BATCH_SIZE, shuffle=False)

    INPUT_SIZE = len(FEATURE_COLS)
    model     = SoccerAgentModel(INPUT_SIZE)
    mse_crit  = nn.MSELoss()
    # BCEWithLogitsLoss + pos_weight para compensar desbalance residual
    action_counts = dataset.Ya.sum(dim=0)
    total_samples = len(dataset)
    pos_weight = torch.zeros(2)
    for i, name in enumerate(['Disparo', 'Pase']):
        pos = max(action_counts[i].item(), 1.0)
        neg = total_samples - pos
        pos_weight[i] = min(neg / pos, 50.0)
    bce_crit  = nn.BCEWithLogitsLoss(pos_weight=pos_weight)
    optimizer = optim.Adam(model.parameters(), lr=LEARNING_RATE)

    for epoch in range(EPOCHS):
        model.train()
        total_loss = total_mse = total_bce = 0.0

        for batch_X, batch_Ymov, batch_Yact in train_loader:
            optimizer.zero_grad()
            pm, pa = model(batch_X)
            loss_mse = mse_crit(pm, batch_Ymov)
            loss_bce = bce_crit(pa, batch_Yact)
            loss = MOVEMENT_LOSS_WEIGHT * loss_mse + ACTION_LOSS_WEIGHT * loss_bce
            loss.backward()
            optimizer.step()

            total_loss += loss.item()
            total_mse  += loss_mse.item()
            total_bce  += loss_bce.item()

        if (epoch + 1) % 30 == 0:
            n = len(train_loader)
            print(f"[{model_name}] Epoch [{epoch+1:3d}/{EPOCHS}] Loss={total_loss/n:.4f} MSE={total_mse/n:.4f} BCE={total_bce/n:.4f}")

    # Evaluacion (rapida)
    model.eval()
    all_pm, all_pa, all_ym, all_ya = [], [], [], []
    with torch.no_grad():
        for batch_X, batch_Ym, batch_Ya in test_loader:
            pm, pa = model(batch_X)
            all_pm.append(pm.cpu().numpy())
            all_pa.append(pa.cpu().numpy())
            all_ym.append(batch_Ym.cpu().numpy())
            all_ya.append(batch_Ya.cpu().numpy())

    pred_mov = np.vstack(all_pm); true_mov = np.vstack(all_ym)
    pred_X = discretize(pred_mov[:, 0]); true_X = discretize(true_mov[:, 0])
    pred_Z = discretize(pred_mov[:, 1]); true_Z = discretize(true_mov[:, 1])
    combined_pred = [f"{x},{z}" for x,z in zip(pred_X, pred_Z)]
    combined_true = [f"{x},{z}" for x,z in zip(true_X, true_Z)]
    acc_mov  = accuracy_score(combined_true, combined_pred)
    prec_mov = precision_score(combined_true, combined_pred, average='macro', zero_division=0)
    rec_mov  = recall_score(combined_true,   combined_pred, average='macro', zero_division=0)
    f1_mov   = f1_score(combined_true,       combined_pred, average='macro', zero_division=0)

    print(classification_report(true_X, pred_X, labels=[-1,0,1],
        target_names=['Izq(-1)','Stop(0)','Der(+1)'], zero_division=0))
    print(classification_report(true_Z, pred_Z, labels=[-1,0,1],
        target_names=['Atras(-1)','Stop(0)','Adelante(+1)'], zero_division=0))
    print(f"[{model_name}] Movimiento Combinado: Acc={acc_mov*100:.1f}%  Prec={prec_mov*100:.1f}%  Rec={rec_mov*100:.1f}%  F1={f1_mov*100:.1f}%")

    n_batches = len(train_loader)
    last_loss = total_loss / n_batches if n_batches > 0 else 0.0

    # Exportar ONNX
    output_path = f"{ONNX_OUTPUT_PREFIX}{model_name}.onnx"
    dummy = torch.randn(1, INPUT_SIZE)
    try:
        torch.onnx.export(
            model, dummy, output_path,
            export_params=True, opset_version=18,
            do_constant_folding=True,
            input_names=['vector_observation'],
            output_names=['continuous_actions', 'discrete_actions'],
            dynamic_axes={
                'vector_observation':  {0: 'batch_size'},
                'continuous_actions':  {0: 'batch_size'},
                'discrete_actions':    {0: 'batch_size'},
            }
        )
        print(f"[{model_name}] Guardado en {output_path}")
    except Exception as e:
        print(f"[{model_name}] ONNX export fallo ({e})")

    return {'acc': acc_mov, 'prec': prec_mov, 'rec': rec_mov, 'f1': f1_mov, 'loss': last_loss}


def train_all(timestamp=''):
    print("Iniciando carga de datos...")
    data_dict, global_mean, global_std = load_and_split_data(CSV_FILES)

    results = {}
    for behavior_name, df_subset in data_dict.items():
        m = train_model(behavior_name, df_subset, global_mean, global_std)
        if m is not None:
            results[behavior_name] = m

    # Tabla comparativa entre sub-modelos
    print("\n" + "="*60)
    print("  COMPARATIVA SUB-MODELOS (MultiModel)")
    print("="*60)
    print(f"  {'Modelo':<12}  {'Acc':>6}  {'Prec':>6}  {'Rec':>6}  {'F1':>6}  {'Loss':>8}")
    print(f"  {'-'*12}  {'-'*6}  {'-'*6}  {'-'*6}  {'-'*6}  {'-'*8}")
    for name, m in results.items():
        print(f"  {name:<12}  {m['acc']*100:6.1f}  {m['prec']*100:6.1f}  {m['rec']*100:6.1f}  {m['f1']*100:6.1f}  {m['loss']:8.4f}")

    # METRICS_BLOCK para compare_logs.py
    print("[METRICS_START]")
    print(f"MODEL=MultiModel")
    print(f"TIMESTAMP={timestamp}")
    for name, m in results.items():
        n = name.upper()
        print(f"ACC_MOV_{n}={m['acc']*100:.2f}")
        print(f"PREC_MOV_{n}={m['prec']*100:.2f}")
        print(f"REC_MOV_{n}={m['rec']*100:.2f}")
        print(f"F1_MOV_{n}={m['f1']*100:.2f}")
        print(f"LOSS_{n}={m['loss']:.4f}")
    print("[METRICS_END]")

    print("\n¡Entrenamiento multi-modelo completado!")


if __name__ == "__main__":
    ts  = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
    log_dir  = os.path.join(os.path.dirname(__file__) or '.', 'logs')
    log_path = os.path.join(log_dir, f'train_multimodel_{ts}.txt')
    tee = Tee(log_path)
    sys.stdout = tee
    try:
        train_all(timestamp=ts)
    finally:
        tee.close()
