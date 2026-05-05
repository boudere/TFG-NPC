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
CSV_FILES = glob.glob('../Assets/SoccerData_Temporal_2026_05_05_18_15_22.csv')

ONNX_OUTPUT_PATH  = '../Assets/SoccerModel_GRU.onnx'
SCALER_OUTPUT     = '../Assets/scaler_gru.json'
EPOCHS        = 150
BATCH_SIZE    = 32
LEARNING_RATE = 0.001

MOVEMENT_LOSS_WEIGHT = 1.0
ACTION_LOSS_WEIGHT   = 2.0
SEQ_LEN = 10


# ============================================================================
# 2. MODELO — Arquitectura GRU
# ============================================================================
class CustomGRUCell(nn.Module):
    def __init__(self, input_size, hidden_size):
        super().__init__()
        self.hidden_size = hidden_size
        self.W_ir = nn.Linear(input_size, hidden_size)
        self.W_hr = nn.Linear(hidden_size, hidden_size, bias=False)
        self.W_iz = nn.Linear(input_size, hidden_size)
        self.W_hz = nn.Linear(hidden_size, hidden_size, bias=False)
        self.W_in = nn.Linear(input_size, hidden_size)
        self.W_hn = nn.Linear(hidden_size, hidden_size, bias=False)

    def forward(self, x, h):
        r = torch.sigmoid(self.W_ir(x) + self.W_hr(h))
        z = torch.sigmoid(self.W_iz(x) + self.W_hz(h))
        n = torch.tanh(self.W_in(x) + r * self.W_hn(h))
        return (1 - z) * n + z * h

class SoccerGRUModel(nn.Module):
    def __init__(self, input_size: int = 40, hidden_size: int = 64):
        super().__init__()
        self.hidden_size = hidden_size
        # Usamos una implementación manual de GRU para que Unity InferenceEngine
        # la soporte 100% como operaciones matemáticas básicas (MatMul, Add, Sigmoid).
        self.gru_cell = CustomGRUCell(input_size, hidden_size)
        
        # Ramas separadas para evitar interferencia
        self.movement_branch = nn.Sequential(
            nn.Linear(hidden_size, 32),
            nn.ReLU(),
            nn.Linear(32, 2),
            nn.Tanh()
        )
        self.action_branch = nn.Sequential(
            nn.Linear(hidden_size, 32),
            nn.ReLU(),
            nn.Linear(32, 2)
        )

    def forward(self, x, h=None):
        # x: [batch, seq_len, 40]
        # h: [1, batch, 64] (opcional)
        batch_size, seq_len, _ = x.size()
        
        if h is None:
            h_t = torch.zeros(batch_size, self.hidden_size, device=x.device)
        else:
            h_t = h.squeeze(0) # de [1, batch, 64] a [batch, 64]
            
        for t in range(seq_len):
            h_t = self.gru_cell(x[:, t, :], h_t)
            
        last_out = h_t
        
        movement = self.movement_branch(last_out)
        actions  = self.action_branch(last_out)
        
        h_new = h_t.unsqueeze(0) # volver a [1, batch, 64]
        return movement, actions, h_new


# ============================================================================
# 3. DATASET
# ============================================================================
class SoccerGRUDataset(Dataset):
    def __init__(self, csv_files: list, seq_len: int = 10):
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
        movement_cols = ['InputX', 'InputZ']
        action_cols   = ['Disparo', 'Pase']

        X_seqs = []
        Ym_seqs = []
        Ya_seqs = []

        for path in csv_files:
            if not os.path.exists(path):
                continue
            tmp = pd.read_csv(path)
            
            # -- CORRECCION DE TIMING --
            LOOKBACK = 5
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
                            found = True
                            break
                    if not found:
                        tmp.loc[idx, action_col] = 0

            # -- FILTRO SPAWN NOISE --
            spawn_mask = (
                (tmp['Aliado1DirX'] == 0) & (tmp['Aliado1DirZ'] == 0) &
                (tmp['Aliado2DirX'] == 0) & (tmp['Aliado2DirZ'] == 0)
            )
            valid_idx = np.where(~spawn_mask)[0]

            if len(valid_idx) < seq_len:
                continue

            for i in range(len(valid_idx) - seq_len + 1):
                idx_start = valid_idx[i]
                idx_end = valid_idx[i + seq_len - 1]
                # Validar contiguidad en el tiempo
                if idx_end - idx_start == seq_len - 1:
                    seq = tmp.iloc[idx_start : idx_end + 1]
                    X_seqs.append(seq[feature_cols].values.flatten())
                    Ym_seqs.append(seq.iloc[-1][movement_cols].values)
                    Ya_seqs.append(seq.iloc[-1][action_cols].values)

        if not X_seqs:
            raise FileNotFoundError("Ningun CSV valido encontrado para generar secuencias.")

        X_arr  = np.array(X_seqs, dtype=np.float32)
        Ym_arr = np.array(Ym_seqs, dtype=np.float32)
        Ya_arr = np.array(Ya_seqs, dtype=np.float32)

        # -- NORMALIZACION (ANTES DE SMOTE) --
        # Reshape a 3D para calcular mean y std correctos sobre todas las observaciones
        X_tensor_raw = torch.tensor(X_arr).view(-1, seq_len, 40)
        self.mean = X_tensor_raw.mean(dim=(0, 1), keepdim=True)
        self.std  = X_tensor_raw.std(dim=(0, 1), keepdim=True)
        self.std[self.std == 0] = 1.0

        # -- SMOTE --
        smote_classes = np.zeros(len(Ya_arr))
        smote_classes[Ya_arr[:, 1] == 1] = 2  # Pase
        smote_classes[Ya_arr[:, 0] == 1] = 1  # Disparo

        class_counts = pd.Series(smote_classes).value_counts().sort_index()
        n_minority = sum(class_counts.get(c, 0) for c in [1, 2])

        if n_minority > 0:
            min_minority_count = min(class_counts.get(c, 0) for c in [1, 2] if class_counts.get(c, 0) > 0)
            k = max(1, min(5, min_minority_count - 1))
            
            majority_n = class_counts.get(0, 1)
            target = max(int(majority_n * 0.15), min_minority_count)

            sampling_strategy = {}
            for c in [1, 2]:
                if class_counts.get(c, 0) > 0:
                    sampling_strategy[c] = max(target, class_counts.get(c, 0))

            smote = SMOTE(sampling_strategy=sampling_strategy, k_neighbors=k, random_state=42)
            
            # Interpolamos features y movimientos simultáneamente
            XY_arr = np.concatenate([X_arr, Ym_arr], axis=1)
            n_orig = len(X_arr)
            
            XY_res, y_res = smote.fit_resample(XY_arr, smote_classes)
            
            X_arr  = XY_res[:, :-2]
            Ym_arr = XY_res[:, -2:]
            
            Ya_arr = np.zeros((len(y_res), 2), dtype=np.float32)
            Ya_arr[y_res == 1, 0] = 1.0
            Ya_arr[y_res == 2, 1] = 1.0
            
            is_synth = np.zeros(len(X_arr), dtype=np.float32)
            is_synth[n_orig:] = 1.0
        else:
            is_synth = np.zeros(len(X_arr), dtype=np.float32)

        # Barajar el dataset
        indices = np.random.permutation(len(X_arr))
        X_arr  = X_arr[indices]
        Ym_arr = Ym_arr[indices]
        Ya_arr = Ya_arr[indices]
        is_synth = is_synth[indices]

        # Reshape a secuencias de 3D
        self.X  = torch.tensor(X_arr).view(-1, seq_len, 40)
        self.Ym = torch.tensor(Ym_arr)
        self.Ya = torch.tensor(Ya_arr)
        self.IsSynth = torch.tensor(is_synth)

        # Aplicar Normalizacion (broadcasting)
        self.X = (self.X - self.mean) / self.std

    def __len__(self):
        return len(self.X)

    def __getitem__(self, idx):
        return self.X[idx], self.Ym[idx], self.Ya[idx], self.IsSynth[idx]


def train(timestamp=''):
    dataset = SoccerGRUDataset(CSV_FILES, seq_len=SEQ_LEN)
    indices = np.arange(len(dataset))
    train_idx, test_idx = train_test_split(indices, test_size=0.2, random_state=42)
    train_loader = DataLoader(Subset(dataset, train_idx), batch_size=BATCH_SIZE, shuffle=True)
    test_loader  = DataLoader(Subset(dataset, test_idx),  batch_size=BATCH_SIZE, shuffle=False)

    with open(SCALER_OUTPUT, "w") as f:
        # Se guarda de [1, 1, 40] a lista simple [40]
        json.dump({"mean": dataset.mean.squeeze().tolist(),
                   "std":  dataset.std.squeeze().tolist()}, f)

    INPUT_SIZE = 40
    model = SoccerGRUModel(input_size=INPUT_SIZE)
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
            pred_movement, pred_actions, _ = model(batch_X) # batch_X es [B, SEQ_LEN, 40]
            
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
    class ExportWrapperGRU(nn.Module):
        def __init__(self, base_model):
            super().__init__()
            self.base = base_model
        def forward(self, x, h):
            movement, action_logits, h_new = self.base(x, h)
            return movement, torch.sigmoid(action_logits), h_new

    export_model = ExportWrapperGRU(model)
    export_model.eval()
    
    # Dummy inputs para Unity inference (seq_len=1)
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
    log_path = os.path.join(log_dir, f'train_gru_{ts}.txt')
    tee = Tee(log_path)
    sys.stdout = tee
    try:
        train(timestamp=ts)
    finally:
        tee.close()
