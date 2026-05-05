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
# TEE
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

ONNX_OUTPUT_PATH  = '../Assets/SoccerModel_RNN.onnx'
SCALER_OUTPUT     = '../Assets/scaler_rnn.json'
EPOCHS        = 150
BATCH_SIZE    = 32
LEARNING_RATE = 0.001

MOVEMENT_LOSS_WEIGHT = 1.0
ACTION_LOSS_WEIGHT   = 2.0


# ============================================================================
# 2. MODELO — Arquitectura RNN Vanilla
# ============================================================================
class CustomRNNCell(nn.Module):
    def __init__(self, input_size, hidden_size):
        super().__init__()
        self.hidden_size = hidden_size
        self.ih = nn.Linear(input_size, hidden_size)
        self.hh = nn.Linear(hidden_size, hidden_size)
        
    def forward(self, x, hx=None):
        if hx is None:
            hx = torch.zeros(x.size(0), self.hidden_size, device=x.device)
        else:
            hx = hx.squeeze(0) # [batch, hidden]
            
        h_next = torch.tanh(self.ih(x) + self.hh(hx))
        return h_next, h_next.unsqueeze(0)

class SoccerRNNModel(nn.Module):
    def __init__(self, input_size: int = 40, hidden_size: int = 64):
        super().__init__()
        self.rnn_cell = CustomRNNCell(input_size, hidden_size)
        
        self.movement_head = nn.Sequential(
            nn.Linear(hidden_size, 2),
            nn.Tanh()
        )
        self.action_head = nn.Sequential(
            nn.Linear(hidden_size, 2),
        )

    def forward(self, x, h=None):
        # x: [batch, seq_len, 40]
        # h: [1, batch, 64] (opcional)
        batch_size = x.size(0)
        seq_len = x.size(1)
        
        if h is None:
            h = torch.zeros(1, batch_size, 64, device=x.device)
            
        hx = h
        # Procesar secuencialmente
        for t in range(seq_len):
            x_t = x[:, t, :]
            _, hx = self.rnn_cell(x_t, hx)
            
        # Tomamos el output del último paso de la secuencia
        last_out = hx.squeeze(0)
        
        movement = self.movement_head(last_out)
        actions  = self.action_head(last_out)
        
        return movement, actions, hx


# ============================================================================
# 3. DATASET
# ============================================================================
class SoccerRNNDataset(Dataset):
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

            # -- PARES PARA RNN (t-1, t) --
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

        # -- SMOTE (en plano) --
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
            
            n_orig = len(raw)
            X_res, y_res = smote.fit_resample(X_sm, y_sm)
            df_res = pd.DataFrame(X_res, columns=smote_cols)

            df_res['Disparo'] = (y_res == 1).astype(int)
            df_res['Pase']    = (y_res == 2).astype(int)
            
            is_synth = np.zeros(len(X_res), dtype=np.float32)
            is_synth[n_orig:] = 1.0
            df_res['is_synthetic'] = is_synth
            
            raw = df_res
        else:
            raw['is_synthetic'] = 0.0

        if '_smote_class' in raw.columns:
            raw.drop('_smote_class', axis=1, inplace=True)

        self.data = raw.sample(frac=1, random_state=42).reset_index(drop=True)

        # Obtener tensor plano [N, 80]
        X_flat = torch.tensor(self.data[feature_cols_combined].values, dtype=torch.float32)
        
        # Reshape a [N, 2, 40]
        self.X = X_flat.reshape(-1, 2, 40)
        
        self.Ym = torch.tensor(self.data[movement_cols].values, dtype=torch.float32)
        self.Ya = torch.tensor(self.data[action_cols].values,   dtype=torch.float32)
        self.IsSynth = torch.tensor(self.data['is_synthetic'].values, dtype=torch.float32)

        # Normalizacion (calculada sobre los 40 features en las dos posiciones, para no deformar)
        # Flatten para calcular la media de 40 features
        X_all_frames = self.X.reshape(-1, 40)
        self.mean = X_all_frames.mean(dim=0)
        self.std  = X_all_frames.std(dim=0)
        self.std[self.std == 0] = 1.0
        
        # Aplicar normalización (broadcasting de [40] a [N, 2, 40] funciona con unsqueeze)
        self.X = (self.X - self.mean.unsqueeze(0).unsqueeze(0)) / self.std.unsqueeze(0).unsqueeze(0)

    def __len__(self):
        return len(self.data)

    def __getitem__(self, idx):
        return self.X[idx], self.Ym[idx], self.Ya[idx], self.IsSynth[idx]


def train(timestamp=''):
    dataset = SoccerRNNDataset(CSV_FILES)
    indices = np.arange(len(dataset))
    train_idx, test_idx = train_test_split(indices, test_size=0.2, random_state=42)
    train_loader = DataLoader(Subset(dataset, train_idx), batch_size=BATCH_SIZE, shuffle=True)
    test_loader  = DataLoader(Subset(dataset, test_idx),  batch_size=BATCH_SIZE, shuffle=False)

    with open(SCALER_OUTPUT, "w") as f:
        json.dump({"mean": dataset.mean.tolist(),
                   "std":  dataset.std.tolist()}, f)

    INPUT_SIZE = 40
    model = SoccerRNNModel(input_size=INPUT_SIZE)
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
            pred_movement, pred_actions, _ = model(batch_X) # batch_X es [B, 2, 40]
            
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

    # Wrapper de exportación ONNX.
    # Unity llamará a este modelo frame a frame, por lo que el input es [1, 1, 40].
    # Además, requerimos que el hidden_state sea una entrada explícita.
    class ExportWrapperRNN(nn.Module):
        def __init__(self, base_model):
            super().__init__()
            self.base = base_model
        def forward(self, x, h):
            # x será de [batch, 1, 40], h será [1, batch, 64]
            movement, action_logits, h_new = self.base(x, h)
            return movement, torch.sigmoid(action_logits), h_new

    export_model = ExportWrapperRNN(model)
    export_model.eval()
    
    # Tensor dummy para inferencia step-by-step
    dummy_x = torch.randn(1, 1, INPUT_SIZE) 
    dummy_h = torch.randn(1, 1, 64) # 64 es el hidden_size
    
    torch.onnx.export(
        export_model, (dummy_x, dummy_h), ONNX_OUTPUT_PATH,
        export_params=True, opset_version=14,
        do_constant_folding=True,
        input_names=['vector_observation', 'hidden_state_in'],
        output_names=['continuous_actions', 'discrete_actions', 'hidden_state_out']
    )

if __name__ == "__main__":
    ts  = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
    log_dir  = os.path.join(os.path.dirname(__file__) or '.', 'logs')
    log_path = os.path.join(log_dir, f'train_rnn_{ts}.txt')
    tee = Tee(log_path)
    sys.stdout = tee
    try:
        train(timestamp=ts)
    finally:
        tee.close()
