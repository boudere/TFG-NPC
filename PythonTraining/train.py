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
from sklearn.metrics import accuracy_score, precision_score, recall_score, classification_report
import os
import json

import glob
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
        self.action_head = nn.Sequential(
            nn.Linear(64, 2),
            nn.Sigmoid()
        )

    def forward(self, x):
        shared   = self.backbone(x)
        movement = self.movement_head(shared)
        actions  = self.action_head(shared)
        return movement, actions


# 3. DATASET
class SoccerDataset(Dataset):
    def __init__(self, df: pd.DataFrame, global_mean: torch.Tensor, global_std: torch.Tensor):
        if len(df) == 0:
            raise ValueError("El DataFrame esta vacio.")

        raw = df.copy()
        
        # ── BALANCEO 1: Clases de movimiento ──
        raw['_mov'] = raw['InputX'].astype(str) + '_' + raw['InputZ'].astype(str)
        mov_counts  = raw['_mov'].value_counts()
        mov_max     = int(mov_counts.max())
        
        balanced = []
        for _, group in raw.groupby('_mov'):
            if len(group) < mov_max:
                group = group.sample(n=mov_max, replace=True, random_state=42)
            balanced.append(group)
            
        df_bal = pd.concat(balanced).reset_index(drop=True)

        # ── BALANCEO 2: Oversampling de Disparo y Pase ──
        n_shoot_pos = int(df_bal['Disparo'].sum())
        n_shoot_neg = len(df_bal) - n_shoot_pos
        if n_shoot_pos > 0 and n_shoot_neg > 0 and n_shoot_pos < n_shoot_neg:
            shoot_pos   = df_bal[df_bal['Disparo'] == 1]
            shoot_extra = shoot_pos.sample(n=n_shoot_neg - n_shoot_pos, replace=True, random_state=42)
            df_bal = pd.concat([df_bal, shoot_extra], ignore_index=True)

        n_pass_pos = int(df_bal['Pase'].sum())
        n_pass_neg = len(df_bal) - n_pass_pos
        if n_pass_pos > 0 and n_pass_neg > 0 and n_pass_pos < n_pass_neg:
            pass_pos   = df_bal[df_bal['Pase'] == 1]
            pass_extra = pass_pos.sample(n=n_pass_neg - n_pass_pos, replace=True, random_state=42)
            df_bal = pd.concat([df_bal, pass_extra], ignore_index=True)

        self.data = df_bal.sample(frac=1, random_state=42).reset_index(drop=True)

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
    bce_crit  = nn.BCELoss()
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
    acc = accuracy_score(combined_true, combined_pred)
    print(f"[{model_name}] Validacion - Accuracy Movimiento Combinado: {acc*100:.1f}%")

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


def train_all():
    print("Iniciando carga de datos...")
    data_dict, global_mean, global_std = load_and_split_data(CSV_FILES)
    
    for behavior_name, df_subset in data_dict.items():
        train_model(behavior_name, df_subset, global_mean, global_std)

if __name__ == "__main__":
    train_all()
