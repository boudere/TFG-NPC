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
                             classification_report, confusion_matrix)
import os
import json
import glob

# ============================================================================
# 1. PARAMETROS
# ============================================================================
CSV_FILES = glob.glob('../Assets/SoccerData_*.csv')

ONNX_OUTPUT_PATH  = '../Assets/SoccerModel_Legacy.onnx'
SCALER_OUTPUT     = '../Assets/scaler_legacy.json'
EPOCHS        = 150
BATCH_SIZE    = 32
LEARNING_RATE = 0.001

# Peso relativo de cada perdida
MOVEMENT_LOSS_WEIGHT = 1.0
ACTION_LOSS_WEIGHT   = 2.0


# ============================================================================
# 2. MODELO — Arquitectura de dos heads (dual-output)
#   Shared backbone  (40 -> 128 -> 64)
#   |-- Movement head: Linear(64,2) + Tanh    -> InputX, InputZ in [-1, 1]
#   |-- Action head:   Linear(64,2) (logits)  -> Shoot, Pass
# ============================================================================
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
        # Acciones binarias (shoot, pass) — SIN Sigmoid aqui,
        # porque BCEWithLogitsLoss lo aplica internamente (mas estable numericamente)
        self.action_head = nn.Sequential(
            nn.Linear(64, 2),
        )

    def forward(self, x):
        shared   = self.backbone(x)
        movement = self.movement_head(shared)   # [B, 2]  InputX, InputZ
        actions  = self.action_head(shared)     # [B, 2]  Shoot, Pass (LOGITS)
        return movement, actions


# ============================================================================
# 3. DATASET
# ============================================================================
class SoccerDataset(Dataset):
    def __init__(self, csv_files: list):
        frames = []
        for path in csv_files:
            if not os.path.exists(path):
                print(f"  AVISO: no se encuentra {path} -- omitido")
                continue
            tmp = pd.read_csv(path)

            # -- FILTRO SPAWN NOISE --
            antes = len(tmp)
            spawn_mask = (
                (tmp['Aliado1DirX'] == 0) & (tmp['Aliado1DirZ'] == 0) &
                (tmp['Aliado2DirX'] == 0) & (tmp['Aliado2DirZ'] == 0)
            )
            tmp = tmp[~spawn_mask]
            despues = len(tmp)

            print(f"  {os.path.basename(path)}: {antes} -> {despues} tras filtro  "
                  f"(Disparo={int(tmp['Disparo'].sum())}, Pase={int(tmp['Pase'].sum())})")
            frames.append(tmp)

        if not frames:
            raise FileNotFoundError("Ningun CSV valido encontrado.")

        raw = pd.concat(frames, ignore_index=True)
        print(f"\n  TOTAL combinado: {len(raw)} filas")
        print(f"  Disparo=1 en total: {int(raw['Disparo'].sum())}")
        print(f"  Pase=1    en total: {int(raw['Pase'].sum())}")

        feature_cols = [
            'RelPorteriaRivalX', 'RelPorteriaRivalZ',
            'RelPorteriaPropiaX', 'RelPorteriaPropiaZ',
            'TienePelota',
            'RelPelotaX', 'RelPelotaZ',
            'DistPelota',
            'TienePelotaEquipo',
            'DistPorteriaContraria',
            'DistPorteriaPropia',
            'PuntuacionPropia', 'PuntuacionContraria',
            'DistPelotaPorteriaPropia',
            'DistAliadoCercano',
            'DistEnemigoCercano',
            'RelAliado1PosX', 'RelAliado1PosZ', 'Aliado1DirX', 'Aliado1DirZ',
            'RelAliado2PosX', 'RelAliado2PosZ', 'Aliado2DirX', 'Aliado2DirZ',
            'RelAliado3PosX', 'RelAliado3PosZ', 'Aliado3DirX', 'Aliado3DirZ',
            'RelEnemigo1PosX', 'RelEnemigo1PosZ', 'Enemigo1DirX', 'Enemigo1DirZ',
            'RelEnemigo2PosX', 'RelEnemigo2PosZ', 'Enemigo2DirX', 'Enemigo2DirZ',
            'RelEnemigo3PosX', 'RelEnemigo3PosZ', 'Enemigo3DirX', 'Enemigo3DirZ',
        ]
        movement_cols = ['InputX', 'InputZ']
        action_cols   = ['Disparo', 'Pase']

        # -- CORRECCION DE TIMING: propagar Disparo/Pase al frame anterior --
        # El Recorder graba la accion en el frame donde la pelota ya salio,
        # asi que TienePelota=0 en ese frame. Pero 1-3 frames antes,
        # TienePelota=1 (el jugador aun tenia la pelota y decidio actuar).
        # Movemos la etiqueta al frame mas cercano con TienePelota=1.
        LOOKBACK = 5  # Maximo de frames hacia atras donde buscar

        shifted_shoot = 0
        shifted_pass  = 0
        for action_col in ['Disparo', 'Pase']:
            action_indices = raw.index[raw[action_col] == 1].tolist()
            for idx in action_indices:
                if raw.loc[idx, 'TienePelota'] == 1:
                    continue  # Ya tiene pelota, no hay que mover nada
                # Buscar hacia atras un frame con TienePelota=1
                found = False
                for offset in range(1, LOOKBACK + 1):
                    prev_idx = idx - offset
                    if prev_idx < 0:
                        break
                    if raw.loc[prev_idx, 'TienePelota'] == 1:
                        raw.loc[idx, action_col] = 0       # quitar del frame original
                        raw.loc[prev_idx, action_col] = 1  # poner en el frame con pelota
                        if action_col == 'Disparo':
                            shifted_shoot += 1
                        else:
                            shifted_pass += 1
                        found = True
                        break
                if not found:
                    # No habia frame con pelota cerca -> eliminar la etiqueta (ruido)
                    raw.loc[idx, action_col] = 0

        clean_shoot = int(raw['Disparo'].sum())
        clean_pass  = int(raw['Pase'].sum())
        print(f"\n  Correccion de timing (label-shift hacia TienePelota=1):")
        print(f"    Disparo: {shifted_shoot} etiquetas movidas -> {clean_shoot} validas")
        print(f"    Pase:    {shifted_pass} etiquetas movidas -> {clean_pass} validas")

        # -- OVERSAMPLING de frames con acciones (Disparo/Pase) --
        action_frames = raw[(raw['Disparo'] == 1) | (raw['Pase'] == 1)]
        normal_frames = raw[(raw['Disparo'] == 0) & (raw['Pase'] == 0)]

        if len(action_frames) > 0:
            target_action_ratio = 0.05
            target_action_count = max(int(len(normal_frames) * target_action_ratio), len(action_frames))
            oversampled_actions = action_frames.sample(n=target_action_count, replace=True, random_state=42)
            raw = pd.concat([normal_frames, oversampled_actions], ignore_index=True)
            print(f"\n  Oversampling acciones: {len(action_frames)} -> {target_action_count} frames de accion")
            print(f"  Dataset total tras oversampling: {len(raw)}")
            print(f"  Disparo=1: {int(raw['Disparo'].sum())} ({raw['Disparo'].mean()*100:.1f}%)")
            print(f"  Pase=1:    {int(raw['Pase'].sum())} ({raw['Pase'].mean()*100:.1f}%)")
        else:
            print("\n  WARNING: No hay frames de Disparo/Pase con pelota en los datos!")
            print("  El modelo NO aprendera a disparar ni pasar.")

        # -- Balanceo de clases sobre movimiento --
        raw['_action'] = raw['InputX'].astype(str) + '_' + raw['InputZ'].astype(str)
        action_counts  = raw['_action'].value_counts()
        max_count      = int(action_counts.max())

        balanced = []
        for _, group in raw.groupby('_action'):
            if len(group) < max_count:
                group = group.sample(n=max_count, replace=True, random_state=42)
            balanced.append(group)

        self.data = pd.concat(balanced).sample(frac=1, random_state=42).reset_index(drop=True)
        print(f"\n  Filas tras balanceo movimiento: {len(self.data)}  (antes: {len(raw)})")

        self.X  = torch.tensor(self.data[feature_cols].values,  dtype=torch.float32)
        self.Ym = torch.tensor(self.data[movement_cols].values, dtype=torch.float32)
        self.Ya = torch.tensor(self.data[action_cols].values,   dtype=torch.float32)

        # Normalizacion de features
        self.mean = self.X.mean(dim=0, keepdim=True)
        self.std  = self.X.std(dim=0,  keepdim=True)
        self.std[self.std == 0] = 1.0
        self.X = (self.X - self.mean) / self.std

    def __len__(self):
        return len(self.data)

    def __getitem__(self, idx):
        return self.X[idx], self.Ym[idx], self.Ya[idx]


# ============================================================================
# 4. HELPERS DE EVALUACION
# ============================================================================
def discretize(values, threshold=0.3):
    """Convierte salida continua del joystick a clase {-1, 0, +1}."""
    result = np.zeros_like(values, dtype=int)
    result[values >  threshold] =  1
    result[values < -threshold] = -1
    return result

def binarize(values, threshold=0.5):
    """Convierte salida sigmoide a clase binaria {0, 1}."""
    return (values >= threshold).astype(int)


# ============================================================================
# 5. ENTRENAMIENTO
# ============================================================================
def train():
    print("=" * 60)
    print("  ENTRENANDO MODELO LEGACY (Monolitico dual-head)")
    print("=" * 60)

    print("\nCargando datasets...")
    dataset = SoccerDataset(CSV_FILES)

    # Split 80 / 20
    indices = np.arange(len(dataset))
    train_idx, test_idx = train_test_split(indices, test_size=0.2, random_state=42)
    train_loader = DataLoader(Subset(dataset, train_idx), batch_size=BATCH_SIZE, shuffle=True)
    test_loader  = DataLoader(Subset(dataset, test_idx),  batch_size=BATCH_SIZE, shuffle=False)
    print(f"\nSplit: {len(train_idx)} train | {len(test_idx)} test")

    # Guardar scaler para Unity
    with open(SCALER_OUTPUT, "w") as f:
        json.dump({"mean": dataset.mean.squeeze().tolist(),
                   "std":  dataset.std.squeeze().tolist()}, f)
    print(f"Scaler guardado en {SCALER_OUTPUT}")

    INPUT_SIZE = 40
    model     = SoccerAgentModel(INPUT_SIZE)
    mse_crit  = nn.MSELoss()

    # Calcular pos_weight para compensar el desbalance extremo de Disparo/Pase
    action_counts = dataset.Ya.sum(dim=0)  # [shoot_count, pass_count]
    total_samples = len(dataset)
    pos_weight = torch.zeros(2)
    for i, name in enumerate(['Disparo', 'Pase']):
        pos = max(action_counts[i].item(), 1.0)
        neg = total_samples - pos
        pw = neg / pos
        pw = min(pw, 50.0)  # Clamp para no desestabilizar
        pos_weight[i] = pw
        print(f"  pos_weight[{name}] = {pw:.1f}  ({int(pos)} positivos / {int(neg)} negativos)")

    bce_crit = nn.BCEWithLogitsLoss(pos_weight=pos_weight)
    optimizer = optim.Adam(model.parameters(), lr=LEARNING_RATE)

    print(f"\nEntrenando {EPOCHS} epochs...")
    for epoch in range(EPOCHS):
        model.train()
        total_loss = total_mse = total_bce = 0.0

        for batch_X, batch_Ym, batch_Ya in train_loader:
            optimizer.zero_grad()

            pred_movement, pred_actions = model(batch_X)

            loss_mse = mse_crit(pred_movement, batch_Ym)
            loss_bce = bce_crit(pred_actions,  batch_Ya)
            loss = MOVEMENT_LOSS_WEIGHT * loss_mse + ACTION_LOSS_WEIGHT * loss_bce

            loss.backward()
            optimizer.step()

            total_loss += loss.item()
            total_mse  += loss_mse.item()
            total_bce  += loss_bce.item()

        if (epoch + 1) % 10 == 0:
            n = len(train_loader)
            print(f"  Epoch [{epoch+1:3d}/{EPOCHS}] "
                  f"Loss={total_loss/n:.4f}  "
                  f"MSE(mov)={total_mse/n:.4f}  "
                  f"BCE(shoot+pass)={total_bce/n:.4f}")

    print("\nEntrenamiento finalizado.")

    # ── EVALUACION ──
    print("\n" + "=" * 60)
    print("  EVALUACION (test 20%)")
    print("=" * 60)

    model.eval()
    all_pred_mov, all_pred_act = [], []
    all_true_mov, all_true_act = [], []

    with torch.no_grad():
        for batch_X, batch_Ym, batch_Ya in test_loader:
            pm, pa = model(batch_X)
            all_pred_mov.append(pm.cpu().numpy())
            all_pred_act.append(torch.sigmoid(pa).cpu().numpy())  # logits -> probabilidades
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

    print("[InputZ — Movimiento Vertical]")
    print(classification_report(true_Z, pred_Z, labels=[-1,0,1],
                                target_names=["Atras(-1)","Quiet(0)","Adelante(+1)"],
                                zero_division=0))

    combined_pred = [f"{x},{z}" for x,z in zip(pred_X, pred_Z)]
    combined_true = [f"{x},{z}" for x,z in zip(true_X, true_Z)]
    acc  = accuracy_score(combined_true, combined_pred)
    prec = precision_score(combined_true, combined_pred, average='macro', zero_division=0)
    rec  = recall_score(combined_true,   combined_pred, average='macro', zero_division=0)
    print(f"[Mov Combinado]  Accuracy={acc*100:.1f}%  Precision={prec*100:.1f}%  Recall={rec*100:.1f}%")

    # -- Acciones binarias --
    pred_shoot = binarize(pred_act[:, 0]);  true_shoot = true_act[:, 0].astype(int)
    pred_pass  = binarize(pred_act[:, 1]);  true_pass  = true_act[:, 1].astype(int)

    print("\n[Disparo]")
    print(classification_report(true_shoot, pred_shoot, labels=[0,1],
                                target_names=["No chuta","Chuta"],
                                zero_division=0))

    print("[Pase]")
    print(classification_report(true_pass, pred_pass, labels=[0,1],
                                target_names=["No pasa","Pasa"],
                                zero_division=0))

    # -- Distribucion de predicciones de acciones --
    print("=" * 60)
    print("  DISTRIBUCION DE PREDICCIONES DE ACCIONES")
    print("=" * 60)
    print(f"\n  Shoot prob — min={pred_act[:,0].min():.4f}  max={pred_act[:,0].max():.4f}  "
          f"mean={pred_act[:,0].mean():.4f}  median={np.median(pred_act[:,0]):.4f}")
    print(f"  Pass  prob — min={pred_act[:,1].min():.4f}  max={pred_act[:,1].max():.4f}  "
          f"mean={pred_act[:,1].mean():.4f}  median={np.median(pred_act[:,1]):.4f}")

    # Histograma de probabilidades
    for name, col_idx in [("Shoot", 0), ("Pass", 1)]:
        probs = pred_act[:, col_idx]
        bins = [0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0]
        hist, _ = np.histogram(probs, bins=bins)
        print(f"\n  Histograma {name}:")
        for i in range(len(hist)):
            bar = '#' * (hist[i] * 50 // max(hist.max(), 1))
            print(f"    [{bins[i]:.1f}-{bins[i+1]:.1f}): {hist[i]:6d}  {bar}")

    print("\n" + "=" * 60)

    # ── EXPORTAR ONNX ──
    # Wrapper que aplica sigmoid a los logits de acciones para que Unity
    # reciba probabilidades [0,1] directamente
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
    try:
        torch.onnx.export(
            export_model, dummy, ONNX_OUTPUT_PATH,
            export_params=True, opset_version=14,
            do_constant_folding=True,
            input_names=['vector_observation'],
            output_names=['continuous_actions', 'discrete_actions'],
            dynamic_axes={
                'vector_observation':  {0: 'batch_size'},
                'continuous_actions':  {0: 'batch_size'},
                'discrete_actions':    {0: 'batch_size'},
            },
            dynamo=False  # Usar exportador legacy (no requiere onnxscript)
        )
        print(f"Modelo ONNX guardado en {ONNX_OUTPUT_PATH}")
    except Exception as e:
        print(f"ONNX export fallo ({e}), guardando como .pt...")
        traced = torch.jit.trace(export_model, dummy)
        pt_path = ONNX_OUTPUT_PATH.replace('.onnx', '.pt')
        traced.save(pt_path)
        print(f"Modelo JIT guardado en {pt_path}")

    print("\n¡Entrenamiento Legacy completado!")


if __name__ == "__main__":
    train()
