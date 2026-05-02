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
CSV_FILES = glob.glob('../Assets/SoccerData_2026_05_02_19_47_14.csv')

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

        # NOTA: AbsMyPosX, AbsMyPosZ, AbsBallPosX, AbsBallPosZ NO están aquí.
        # Son columnas de visualización (heatmap) — nunca se usan como features.
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
        ]  # 40 features exactas
        assert len(feature_cols) == 40, f"Se esperaban 40 features, hay {len(feature_cols)}"
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

        # -- SMOTE: Synthetic Minority Oversampling Technique --
        # Genera muestras sintéticas interpolando entre vecinos de la clase
        # minoritaria, en lugar de duplicar filas existentes.
        # Clase combinada: 0=sin acción, 1=Disparo, 2=Pase
        raw['_smote_class'] = 0
        raw.loc[raw['Pase'] == 1, '_smote_class'] = 2
        raw.loc[raw['Disparo'] == 1, '_smote_class'] = 1  # prioridad si ambos

        smote_cols = feature_cols + movement_cols

        class_counts = raw['_smote_class'].value_counts().sort_index()
        print(f"\n  Distribución de clases antes de SMOTE:")
        for cls, name in [(0, 'Sin acción'), (1, 'Disparo'), (2, 'Pase')]:
            print(f"    {name} ({cls}): {class_counts.get(cls, 0)}")

        n_minority = sum(class_counts.get(c, 0) for c in [1, 2])
        if n_minority > 0:
            X_sm = raw[smote_cols].values
            y_sm = raw['_smote_class'].values

            # k_neighbors no puede superar el nº de muestras de la clase más pequeña - 1
            min_minority_count = min(
                class_counts.get(c, 0) for c in [1, 2] if class_counts.get(c, 0) > 0
            )
            k = min(5, min_minority_count - 1)
            k = max(k, 1)

            # Objetivo: llevar cada clase minoritaria al 15% de la mayoritaria
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

            # Reconstruir DataFrame con las columnas necesarias
            df_res = pd.DataFrame(X_res, columns=smote_cols)

            # Discretizar InputX/InputZ sintéticos al valor original más cercano {-1, 0, 1}
            for col in movement_cols:
                df_res[col] = df_res[col].apply(
                    lambda v: 1.0 if v > 0.5 else (-1.0 if v < -0.5 else 0.0)
                )

            df_res['Disparo'] = (y_res == 1).astype(int)
            df_res['Pase']    = (y_res == 2).astype(int)

            raw = df_res
            print(f"\n  SMOTE aplicado (k_neighbors={k}, target={target}):")
            print(f"  Dataset total tras SMOTE: {len(raw)}")
            print(f"  Disparo=1: {int(raw['Disparo'].sum())} ({raw['Disparo'].mean()*100:.1f}%)")
            print(f"  Pase=1:    {int(raw['Pase'].sum())} ({raw['Pase'].mean()*100:.1f}%)")
        else:
            print("\n  WARNING: No hay frames de Disparo/Pase con pelota en los datos!")
            print("  El modelo NO aprenderá a disparar ni pasar.")

        if '_smote_class' in raw.columns:
            raw.drop('_smote_class', axis=1, inplace=True)

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
def train(timestamp=''):
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

    n_batches = len(train_loader)
    last_loss = total_loss / n_batches if n_batches > 0 else 0.0
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
    f1   = f1_score(combined_true,       combined_pred, average='macro', zero_division=0)
    print(f"[Mov Combinado]  Accuracy={acc*100:.1f}%  Prec={prec*100:.1f}%  Rec={rec*100:.1f}%  F1={f1*100:.1f}%")

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

    # -- Metricas escalares para METRICS_BLOCK --
    acc_shoot  = accuracy_score(true_shoot, pred_shoot)
    prec_shoot = precision_score(true_shoot, pred_shoot, average='binary', zero_division=0)
    rec_shoot  = recall_score(true_shoot, pred_shoot, average='binary', zero_division=0)
    f1_shoot   = f1_score(true_shoot, pred_shoot, average='binary', zero_division=0)
    acc_pass   = accuracy_score(true_pass, pred_pass)
    prec_pass  = precision_score(true_pass, pred_pass, average='binary', zero_division=0)
    rec_pass   = recall_score(true_pass, pred_pass, average='binary', zero_division=0)
    f1_pass    = f1_score(true_pass, pred_pass, average='binary', zero_division=0)

    print("[METRICS_START]")
    print(f"MODEL=Legacy")
    print(f"TIMESTAMP={timestamp}")
    print(f"ACC_MOV={acc*100:.2f}")
    print(f"PREC_MOV={prec*100:.2f}")
    print(f"REC_MOV={rec*100:.2f}")
    print(f"F1_MOV={f1*100:.2f}")
    print(f"ACC_SHOOT={acc_shoot*100:.2f}")
    print(f"PREC_SHOOT={prec_shoot*100:.2f}")
    print(f"REC_SHOOT={rec_shoot*100:.2f}")
    print(f"F1_SHOOT={f1_shoot*100:.2f}")
    print(f"ACC_PASS={acc_pass*100:.2f}")
    print(f"PREC_PASS={prec_pass*100:.2f}")
    print(f"REC_PASS={rec_pass*100:.2f}")
    print(f"F1_PASS={f1_pass*100:.2f}")
    print(f"LOSS_FINAL={last_loss:.4f}")
    print("[METRICS_END]")

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
    ts  = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
    log_dir  = os.path.join(os.path.dirname(__file__) or '.', 'logs')
    log_path = os.path.join(log_dir, f'train_legacy_{ts}.txt')
    tee = Tee(log_path)
    sys.stdout = tee
    try:
        train(timestamp=ts)
    finally:
        tee.close()
