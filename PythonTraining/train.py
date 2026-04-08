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

# 1. PARAMETERS
CSV_FILES = [
    '../Assets/SoccerData_2026_03_31_19_33_06.csv',   # 3494 filas — dataset limpio
    '../Assets/SoccerData_2026_03_31_20_20_59.csv',   # 3732 filas — con Shoot/Pass
]
ONNX_OUTPUT_PATH = '../Assets/SoccerModel.onnx'
EPOCHS        = 150
BATCH_SIZE    = 32
LEARNING_RATE = 0.001

# Peso relativo de cada perdida
MOVEMENT_LOSS_WEIGHT = 1.0
ACTION_LOSS_WEIGHT   = 2.0

# 2. MODELO — Arquitectura de dos heads (dual-output)
#   Shared backbone  (40 → 128 → 64)
#   ├─ Movement head: Linear(64,2) + Tanh   → InputX, InputZ ∈ [-1, 1]
#   └─ Action head:   Linear(64,2) + Sigmoid → Shoot, Pass   ∈ [0, 1]
class SoccerAgentModel(nn.Module):
    def __init__(self, input_size: int):
        super().__init__()
        self.backbone = nn.Sequential(
            nn.Linear(input_size, 128),
            nn.ReLU(),
            nn.Linear(128, 64),
            nn.ReLU(),
        )
        # Movimiento continuo (-1 a 1)
        self.movement_head = nn.Sequential(
            nn.Linear(64, 2),
            nn.Tanh()
        )
        # Acciones binarias (shoot, pass)
        self.action_head = nn.Sequential(
            nn.Linear(64, 2),
            nn.Sigmoid()
        )

    def forward(self, x):
        shared   = self.backbone(x)
        movement = self.movement_head(shared)   # [B, 2]  InputX, InputZ
        actions  = self.action_head(shared)     # [B, 2]  Shoot,  Pass
        return movement, actions


# 3. DATASET
class SoccerDataset(Dataset):
    def __init__(self, csv_files: list):
        # Cargar y concatenar todos los CSVs
        frames = []
        for path in csv_files:
            if not os.path.exists(path):
                print(f"  AVISO: no se encuentra {path} — omitido")
                continue
            tmp = pd.read_csv(path)
            print(f"  Cargado {path}  ({len(tmp)} filas, "
                  f"Shoot={int(tmp['ActionShoot'].sum())}, Pass={int(tmp['ActionPass'].sum())})") 
            frames.append(tmp)

        if not frames:
            raise FileNotFoundError("Ningun CSV valido encontrado.")

        raw = pd.concat(frames, ignore_index=True)
        print(f"  TOTAL combinado: {len(raw)} filas")
        print(f"  ActionShoot=1 en total: {int(raw['ActionShoot'].sum())}")
        print(f"  ActionPass=1  en total: {int(raw['ActionPass'].sum())}\n")

        feature_cols = [
            # Estado propio
            'MyPosX', 'MyPosZ',
            'MyFacingX', 'MyFacingZ',
            'MyHasBall',
            # Pelota
            'BallPosX', 'BallPosZ',
            'DistToBall',
            'HasBallTeam',
            # Porterías y marcador
            'DistToRivalGoal',
            'DistToOwnGoal',
            'ScoreTeam1', 'ScoreTeam2',
            'DistBallToOwnGoal',
            # Distancias resumen
            'DistClosestAlly',
            'DistClosestEnemy',
            # 3 aliados más cercanos: posición + dirección
            'Ally1PosX', 'Ally1PosZ', 'Ally1DirX', 'Ally1DirZ',
            'Ally2PosX', 'Ally2PosZ', 'Ally2DirX', 'Ally2DirZ',
            'Ally3PosX', 'Ally3PosZ', 'Ally3DirX', 'Ally3DirZ',
            # 3 enemigos más cercanos: posición + dirección
            'Enemy1PosX', 'Enemy1PosZ', 'Enemy1DirX', 'Enemy1DirZ',
            'Enemy2PosX', 'Enemy2PosZ', 'Enemy2DirX', 'Enemy2DirZ',
            'Enemy3PosX', 'Enemy3PosZ', 'Enemy3DirX', 'Enemy3DirZ',
        ]
        movement_cols = ['InputX', 'InputZ']
        action_cols   = ['ActionShoot', 'ActionPass']

        # --- Balanceo de clases sobre las acciones de movimiento ----------
        raw['_action'] = raw['InputX'].astype(str) + '_' + raw['InputZ'].astype(str)
        action_counts  = raw['_action'].value_counts()
        max_count      = int(action_counts.max())
        print(f"Distribución de acciones ANTES del balanceo:\n{action_counts.to_string()}\n")

        balanced = []
        for _, group in raw.groupby('_action'):
            if len(group) < max_count:
                group = group.sample(n=max_count, replace=True, random_state=42)
            balanced.append(group)

        self.data = pd.concat(balanced).sample(frac=1, random_state=42).reset_index(drop=True)
        print(f"Filas tras balanceo: {len(self.data)}  (antes: {len(raw)})\n")
        # ------------------------------------------------------------------

        self.X  = torch.tensor(self.data[feature_cols].values,  dtype=torch.float32)
        self.Ym = torch.tensor(self.data[movement_cols].values,  dtype=torch.float32)
        self.Ya = torch.tensor(self.data[action_cols].values,    dtype=torch.float32)

        # Normalización de features
        self.mean = self.X.mean(dim=0, keepdim=True)
        self.std  = self.X.std(dim=0,  keepdim=True)
        self.std[self.std == 0] = 1.0
        self.X = (self.X - self.mean) / self.std

    def __len__(self):
        return len(self.data)

    def __getitem__(self, idx):
        # Devuelve (features, labels_movimiento, labels_accion)
        return self.X[idx], self.Ym[idx], self.Ya[idx]



# 4. HELPERS DE EVALUACIÓN
def discretize(values, threshold=0.3):
    """Convierte salida continua del joystick a clase {-1, 0, +1}."""
    result = np.zeros_like(values, dtype=int)
    result[values >  threshold] =  1
    result[values < -threshold] = -1
    return result

def binarize(values, threshold=0.5):
    """Convierte salida sigmoide a clase binaria {0, 1}."""
    return (values >= threshold).astype(int)


# 5. ENTRENAMIENTO
def train():
    print("\n=== Cargando datasets ===")
    dataset = SoccerDataset(CSV_FILES)

    # Split 80 / 20
    indices = np.arange(len(dataset))
    train_idx, test_idx = train_test_split(indices, test_size=0.2, random_state=42)
    train_loader = DataLoader(Subset(dataset, train_idx), batch_size=BATCH_SIZE, shuffle=True)
    test_loader  = DataLoader(Subset(dataset, test_idx),  batch_size=BATCH_SIZE, shuffle=False)
    print(f"Split: {len(train_idx)} train | {len(test_idx)} test\n")

    # Guardar scaler para Unity
    scaler_path = os.path.join(os.path.dirname(ONNX_OUTPUT_PATH), "scaler.json")
    with open(scaler_path, "w") as f:
        json.dump({"mean": dataset.mean.squeeze().tolist(),
                   "std":  dataset.std.squeeze().tolist()}, f)
    print(f"Scaler guardado en {scaler_path}")

    INPUT_SIZE = 40
    model     = SoccerAgentModel(INPUT_SIZE)
    mse_crit  = nn.MSELoss()
    bce_crit  = nn.BCELoss()
    optimizer = optim.Adam(model.parameters(), lr=LEARNING_RATE)

    print("Iniciando entrenamiento...\n")
    for epoch in range(EPOCHS):
        model.train()
        total_loss = total_mse = total_bce = 0.0

        for batch_X, batch_Ymovimiento, batch_Yaccion in train_loader:
            optimizer.zero_grad()

            pred_movement, pred_actions = model(batch_X)

            loss_mse = mse_crit(pred_movement, batch_Ymovimiento)
            loss_bce = bce_crit(pred_actions,  batch_Yaccion)
            loss = MOVEMENT_LOSS_WEIGHT * loss_mse + ACTION_LOSS_WEIGHT * loss_bce

            loss.backward()
            optimizer.step()

            total_loss += loss.item()
            total_mse  += loss_mse.item()
            total_bce  += loss_bce.item()

        if (epoch + 1) % 10 == 0:
            n = len(train_loader)
            print(f"Epoch [{epoch+1:3d}/{EPOCHS}] "
                  f"Loss={total_loss/n:.4f}  "
                  f"MSE(mov)={total_mse/n:.4f}  "
                  f"BCE(shoot+pass)={total_bce/n:.4f}")

    print("\nEntrenamiento finalizado.")

    # 6. VALIDACIÓN SOBRE EL 20%
    print("\n" + "="*60)
    print("         VALIDATION RESULTS  (test 20%)")
    print("="*60)

    model.eval()
    all_pred_mov, all_pred_act = [], []
    all_true_mov, all_true_act = [], []

    with torch.no_grad():
        for batch_X, batch_Ym, batch_Ya in test_loader:
            pm, pa = model(batch_X)
            all_pred_mov.append(pm.cpu().numpy())
            all_pred_act.append(pa.cpu().numpy())
            all_true_mov.append(batch_Ym.cpu().numpy())
            all_true_act.append(batch_Ya.cpu().numpy())

    pred_mov = np.vstack(all_pred_mov)
    pred_act = np.vstack(all_pred_act)
    true_mov = np.vstack(all_true_mov)
    true_act = np.vstack(all_true_act)

    # -- Movimiento (discretizado) --
    pred_X = discretize(pred_mov[:, 0]);  true_X = discretize(true_mov[:, 0])
    pred_Z = discretize(pred_mov[:, 1]);  true_Z = discretize(true_mov[:, 1])

    print("\n[InputX — Movimiento Horizontal]")
    print(classification_report(true_X, pred_X, labels=[-1,0,1],
                                target_names=["Izq(-1)","Quiet(0)","Der(+1)"],
                                zero_division=0))

    print("[InputZ — Movimiento Vertical / Adelante-Atrás]")
    print(classification_report(true_Z, pred_Z, labels=[-1,0,1],
                                target_names=["Atrás(-1)","Quiet(0)","Adelante(+1)"],
                                zero_division=0))

    combined_pred  = [f"{x},{z}" for x,z in zip(pred_X, pred_Z)]
    combined_true  = [f"{x},{z}" for x,z in zip(true_X, true_Z)]
    acc  = accuracy_score(combined_true, combined_pred)
    prec = precision_score(combined_true, combined_pred, average='macro', zero_division=0)
    rec  = recall_score(combined_true,   combined_pred, average='macro', zero_division=0)
    print(f"[Acción Combinada Mov]  Accuracy={acc*100:.1f}%  Precision={prec*100:.1f}%  Recall={rec*100:.1f}%")

    # -- Acciones binarias --
    pred_shoot = binarize(pred_act[:, 0]);  true_shoot = true_act[:, 0].astype(int)
    pred_pass  = binarize(pred_act[:, 1]);  true_pass  = true_act[:, 1].astype(int)

    print("\n[ActionShoot — Chute a portería]")
    print(classification_report(true_shoot, pred_shoot, labels=[0,1],
                                target_names=["No chuta","Chuta"],
                                zero_division=0))

    print("[ActionPass — Pase]")
    print(classification_report(true_pass, pred_pass, labels=[0,1],
                                target_names=["No pasa","Pasa"],
                                zero_division=0))

    print("="*60 + "\n")

    # 7. EXPORTAR ONNX
    model.eval()
    dummy = torch.randn(1, INPUT_SIZE)
    try:
        # El modelo tiene dos salidas — hay que registrar ambos output_names
        torch.onnx.export(
            model, dummy, ONNX_OUTPUT_PATH,
            export_params=True, opset_version=14,
            do_constant_folding=True,
            input_names=['vector_observation'],
            output_names=['continuous_actions', 'discrete_actions'],
            dynamic_axes={
                'vector_observation':  {0: 'batch_size'},
                'continuous_actions':  {0: 'batch_size'},
                'discrete_actions':    {0: 'batch_size'},
            }
        )
        print(f"Modelo ONNX guardado en {ONNX_OUTPUT_PATH}")
    except Exception as e:
        print(f"ONNX export falló ({e}), guardando como .pt...")
        traced = torch.jit.trace(model, dummy)
        pt_path = ONNX_OUTPUT_PATH.replace('.onnx', '.pt')
        traced.save(pt_path)
        print(f"Modelo JIT guardado en {pt_path}")


if __name__ == "__main__":
    train()
