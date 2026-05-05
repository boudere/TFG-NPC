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
                             f1_score, classification_report, confusion_matrix)
import os
import json
import glob
import datetime
from imblearn.over_sampling import SMOTE

# ============================================================================
# TEE — escribe en consola Y en fichero de log simultaneamente
# ============================================================================
class Tee:
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

ONNX_OUTPUT_PATH  = '../Assets/SoccerModel_Sliding.onnx'
SCALER_OUTPUT     = '../Assets/scaler_sliding.json'
EPOCHS        = 150
BATCH_SIZE    = 32
LEARNING_RATE = 0.001

MOVEMENT_LOSS_WEIGHT = 1.0
ACTION_LOSS_WEIGHT   = 2.0


# ============================================================================
# 2. MODELO — Arquitectura Sliding Window (80 inputs)
# ============================================================================
class SoccerSlidingAgentModel(nn.Module):
    def __init__(self, input_size: int):
        super().__init__()
        self.movement_branch = nn.Sequential(
            nn.Linear(input_size, 128),
            nn.ReLU(),
            nn.Linear(128, 64),
            nn.ReLU(),
            nn.Linear(64, 2),
            nn.Tanh()
        )
        self.action_branch = nn.Sequential(
            nn.Linear(input_size, 128),
            nn.ReLU(),
            nn.Linear(128, 64),
            nn.ReLU(),
            nn.Linear(64, 2)
        )

    def forward(self, x):
        movement = self.movement_branch(x)
        actions  = self.action_branch(x)
        return movement, actions


# ============================================================================
# 3. DATASET
# ============================================================================
class SoccerSlidingDataset(Dataset):
    def __init__(self, csv_files: list):
        frames = []
        for path in csv_files:
            if not os.path.exists(path):
                continue
            tmp = pd.read_csv(path)
            
            # -- CORRECCION DE TIMING (antes de filtrar y concatenar) --
            LOOKBACK = 5
            shifted_shoot = 0
            shifted_pass  = 0
            for action_col in ['Disparo', 'Pase']:
                action_indices = tmp.index[tmp[action_col] == 1].tolist()
                for idx in action_indices:
                    if tmp.loc[idx, 'TienePelota'] == 1:
                        continue
                    found = False
                    for offset in range(1, LOOKBACK + 1):
                        prev_idx = idx - offset
                        if prev_idx < 0:
                            break
                        if tmp.loc[prev_idx, 'TienePelota'] == 1:
                            tmp.loc[idx, action_col] = 0
                            tmp.loc[prev_idx, action_col] = 1
                            if action_col == 'Disparo': shifted_shoot += 1
                            else: shifted_pass += 1
                            found = True
                            break
                    if not found:
                        tmp.loc[idx, action_col] = 0

            # -- SLIDING WINDOW (Concatenar t-1 y t) --
            feature_cols = [
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
            
            # Crear DataFrame shiftado
            tmp_prev = tmp[feature_cols].shift(1).add_suffix('_prev')
            tmp_combined = pd.concat([tmp_prev, tmp], axis=1)
            
            # -- FILTRO SPAWN NOISE --
            spawn_mask = (
                (tmp['Aliado1DirX'] == 0) & (tmp['Aliado1DirZ'] == 0) &
                (tmp['Aliado2DirX'] == 0) & (tmp['Aliado2DirZ'] == 0)
            )
            
            # Eliminar la primera fila (NaN) y las filas de spawn noise
            valid_mask = (~spawn_mask) & (tmp_prev[feature_cols[0] + '_prev'].notna())
            tmp_combined = tmp_combined[valid_mask]
            
            frames.append(tmp_combined)

        if not frames:
            raise FileNotFoundError("Ningun CSV valido encontrado.")

        raw = pd.concat(frames, ignore_index=True)
        
        feature_cols_combined = [c + '_prev' for c in feature_cols] + feature_cols
        movement_cols = ['InputX', 'InputZ']
        action_cols   = ['Disparo', 'Pase']

        # -- NORMALIZACION (ANTES DE SMOTE) --
        X_raw = torch.tensor(raw[feature_cols_combined].values, dtype=torch.float32)
        self.mean = X_raw.mean(dim=0, keepdim=True)
        self.std  = X_raw.std(dim=0, keepdim=True)
        self.std[self.std == 0] = 1.0

        # -- SMOTE --
        raw['_smote_class'] = 0
        raw.loc[raw['Pase'] == 1, '_smote_class'] = 2
        raw.loc[raw['Disparo'] == 1, '_smote_class'] = 1

        smote_cols = feature_cols_combined + movement_cols

        class_counts = raw['_smote_class'].value_counts().sort_index()
        n_minority = sum(class_counts.get(c, 0) for c in [1, 2])
        if n_minority > 0:
            X_sm = raw[smote_cols].values
            y_sm = raw['_smote_class'].values

            min_minority_count = min(
                class_counts.get(c, 0) for c in [1, 2] if class_counts.get(c, 0) > 0
            )
            k = min(5, min_minority_count - 1)
            k = max(k, 1)

            majority_n = class_counts.get(0, 1)
            target = max(int(majority_n * 0.15), min_minority_count)

            sampling_strategy = {}
            for c in [1, 2]:
                if class_counts.get(c, 0) > 0:
                    sampling_strategy[c] = max(target, class_counts.get(c, 0))

            smote = SMOTE(
                sampling_strategy=sampling_strategy,
                k_neighbors=k,
                random_state=42
            )
            X_res, y_res = smote.fit_resample(X_sm, y_sm)
            df_res = pd.DataFrame(X_res, columns=smote_cols)

            df_res['Disparo'] = (y_res == 1).astype(int)
            df_res['Pase']    = (y_res == 2).astype(int)
            
            # Etiquetar datos sinteticos generados por SMOTE
            n_orig = len(raw)
            is_synth = np.zeros(len(X_res), dtype=np.float32)
            is_synth[n_orig:] = 1.0
            df_res['is_synthetic'] = is_synth

            raw = df_res
        else:
            raw['is_synthetic'] = 0.0

        if '_smote_class' in raw.columns:
            raw.drop('_smote_class', axis=1, inplace=True)

        self.data = raw.sample(frac=1, random_state=42).reset_index(drop=True)

        self.X  = torch.tensor(self.data[feature_cols_combined].values,  dtype=torch.float32)
        self.Ym = torch.tensor(self.data[movement_cols].values, dtype=torch.float32)
        self.Ya = torch.tensor(self.data[action_cols].values,   dtype=torch.float32)
        self.IsSynth = torch.tensor(self.data['is_synthetic'].values, dtype=torch.float32)

        self.X = (self.X - self.mean) / self.std

    def __len__(self):
        return len(self.data)

    def __getitem__(self, idx):
        return self.X[idx], self.Ym[idx], self.Ya[idx], self.IsSynth[idx]


def train(timestamp=''):
    dataset = SoccerSlidingDataset(CSV_FILES)
    indices = np.arange(len(dataset))
    train_idx, test_idx = train_test_split(indices, test_size=0.2, random_state=42)
    train_loader = DataLoader(Subset(dataset, train_idx), batch_size=BATCH_SIZE, shuffle=True)
    test_loader  = DataLoader(Subset(dataset, test_idx),  batch_size=BATCH_SIZE, shuffle=False)

    with open(SCALER_OUTPUT, "w") as f:
        json.dump({"mean": dataset.mean.squeeze().tolist(),
                   "std":  dataset.std.squeeze().tolist()}, f)

    INPUT_SIZE = 80
    model = SoccerSlidingAgentModel(INPUT_SIZE)
    mse_crit  = nn.MSELoss()

    action_counts = dataset.Ya.sum(dim=0)
    total_samples = len(dataset)
    pos_weight = torch.zeros(2)
    for i in range(2):
        pos = max(action_counts[i].item(), 1.0)
        pw = min((total_samples - pos) / pos, 50.0)
        pos_weight[i] = pw

    bce_crit = nn.BCEWithLogitsLoss(pos_weight=pos_weight)
    optimizer = optim.Adam(model.parameters(), lr=LEARNING_RATE)

    for epoch in range(EPOCHS):
        model.train()
        for batch_X, batch_Ym, batch_Ya, batch_IsSynth in train_loader:
            optimizer.zero_grad()
            pred_movement, pred_actions = model(batch_X)
            
            # Solo entrenar movimiento en datos reales (no sinteticos)
            orig_mask = (batch_IsSynth == 0.0)
            if orig_mask.sum() > 0:
                loss_mse = mse_crit(pred_movement[orig_mask], batch_Ym[orig_mask])
            else:
                loss_mse = torch.tensor(0.0, device=batch_X.device)
                
            loss_bce = bce_crit(pred_actions,  batch_Ya)
            loss = MOVEMENT_LOSS_WEIGHT * loss_mse + ACTION_LOSS_WEIGHT * loss_bce
            
            if loss.item() > 0:
                loss.backward()
                optimizer.step()

    class ExportWrapper(nn.Module):
        def __init__(self, base_model):
            super().__init__()
            self.base = base_model
        def forward(self, x):
            movement, action_logits = self.base(x)
            return movement, torch.sigmoid(action_logits)

    export_model = ExportWrapper(model)
    export_model.eval()
    dummy = torch.randn(1, INPUT_SIZE)
    torch.onnx.export(
        export_model, dummy, ONNX_OUTPUT_PATH,
        export_params=True, opset_version=14,
        do_constant_folding=True,
        input_names=['vector_observation'],
        output_names=['continuous_actions', 'discrete_actions']
    )

if __name__ == "__main__":
    ts  = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
    log_dir  = os.path.join(os.path.dirname(__file__) or '.', 'logs')
    log_path = os.path.join(log_dir, f'train_sliding_{ts}.txt')
    tee = Tee(log_path)
    sys.stdout = tee
    try:
        train(timestamp=ts)
    finally:
        tee.close()
