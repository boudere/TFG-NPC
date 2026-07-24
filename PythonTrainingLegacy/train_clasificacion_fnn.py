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

ONNX_OUTPUT_PATH  = '../Assets/SoccerModel_ClasificacionFNN.onnx'
SCALER_OUTPUT     = '../Assets/scaler_clasificacion_fnn.json'
EPOCHS        = 150
BATCH_SIZE    = 32
LEARNING_RATE = 0.001

# Peso relativo de cada perdida
MOVEMENT_LOSS_WEIGHT = 1.0
ACTION_LOSS_WEIGHT   = 2.0


# ============================================================================
# 2. MODELO — Arquitectura de dos heads de CLASIFICACION
#   Shared backbone  (40 -> 128 -> 64)
#   |-- Movement head: Linear(64, 9) (logits 9 clases de mov)
#   |-- Action head:   Linear(64, 2) (logits Shoot, Pass)
# ============================================================================
class SoccerAgentModel(nn.Module):
    def __init__(self, input_size: int):
        super().__init__()
        self.backbone = nn.Sequential(
            nn.Linear(input_size, 128),
            nn.ReLU(),
            nn.Dropout(0.2),
            nn.Linear(128, 64),
            nn.ReLU(),
            nn.Dropout(0.2),
        )
        # Movimiento 9 clases discretas
        self.movement_head = nn.Sequential(
            nn.Linear(64, 9)
        )
        # Acciones binarias (shoot, pass, robok, robol) ── SIN Sigmoid aqui,
        # porque BCEWithLogitsLoss lo aplica internamente
        self.action_head = nn.Sequential(
            nn.Linear(64, 4),
        )

    def forward(self, x):
        shared   = self.backbone(x)
        movement = self.movement_head(shared)   # [B, 9] (LOGITS)
        actions  = self.action_head(shared)     # [B, 4]  Shoot, Pass, RoboK, RoboL (LOGITS)
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

            # -- Asegurar presencia de columnas RoboK y RoboL --
            if 'RoboK' not in tmp.columns:
                tmp['RoboK'] = 0
            if 'RoboL' not in tmp.columns:
                tmp['RoboL'] = 0

            print(f"  {os.path.basename(path)}: {antes} -> {despues} tras filtro  "
                  f"(Disparo={int(tmp['Disparo'].sum())}, Pase={int(tmp['Pase'].sum())}, "
                  f"RoboK={int(tmp['RoboK'].sum())}, RoboL={int(tmp['RoboL'].sum())})")
            frames.append(tmp)

        if not frames:
            raise FileNotFoundError("Ningun CSV valido encontrado.")

        raw = pd.concat(frames, ignore_index=True)
        print(f"\n  TOTAL combinado: {len(raw)} filas")
        print(f"  Disparo=1 en total: {int(raw['Disparo'].sum())}")
        print(f"  Pase=1    en total: {int(raw['Pase'].sum())}")
        print(f"  RoboK=1   en total: {int(raw['RoboK'].sum())}")
        print(f"  RoboL=1   en total: {int(raw['RoboL'].sum())}")

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
        action_cols   = ['Disparo', 'Pase', 'RoboK', 'RoboL']

        # -- CORRECCION DE TIMING: propagar Disparo/Pase al frame anterior --
        LOOKBACK = 5  # Maximo de frames hacia atras donde buscar

        shifted_shoot = 0
        shifted_pass  = 0
        for action_col in ['Disparo', 'Pase']:
            action_indices = raw.index[raw[action_col] == 1].tolist()
            for idx in action_indices:
                if raw.loc[idx, 'TienePelota'] == 1:
                    continue
                found = False
                for offset in range(1, LOOKBACK + 1):
                    prev_idx = idx - offset
                    if prev_idx < 0:
                        break
                    if raw.loc[prev_idx, 'TienePelota'] == 1:
                        raw.loc[idx, action_col] = 0
                        raw.loc[prev_idx, action_col] = 1
                        if action_col == 'Disparo':
                            shifted_shoot += 1
                        else:
                            shifted_pass += 1
                        found = True
                        break
                if not found:
                    raw.loc[idx, action_col] = 0

        clean_shoot = int(raw['Disparo'].sum())
        clean_pass  = int(raw['Pase'].sum())
        print(f"\n  Correccion de timing (label-shift hacia TienePelota=1):")
        print(f"    Disparo: {shifted_shoot} etiquetas movidas -> {clean_shoot} validas")
        print(f"    Pase:    {shifted_pass} etiquetas movidas -> {clean_pass} validas")

        # -- SMOTE: Synthetic Minority Oversampling Technique --
        raw['_smote_class'] = 0
        raw.loc[raw['RoboL'] == 1, '_smote_class'] = 4
        raw.loc[raw['RoboK'] == 1, '_smote_class'] = 3
        raw.loc[raw['Pase'] == 1, '_smote_class'] = 2
        raw.loc[raw['Disparo'] == 1, '_smote_class'] = 1  # prioridad si ambos

        smote_cols = feature_cols + movement_cols

        class_counts = raw['_smote_class'].value_counts().sort_index()
        print(f"\n  Distribución de clases antes de SMOTE:")
        for cls, name in [(0, 'Sin acción'), (1, 'Disparo'), (2, 'Pase'), (3, 'RoboK'), (4, 'RoboL')]:
            print(f"    {name} ({cls}): {class_counts.get(cls, 0)}")

        n_minority = sum(class_counts.get(c, 0) for c in [1, 2, 3, 4])
        if n_minority > 0:
            X_sm = raw[smote_cols].values
            y_sm = raw['_smote_class'].values

            min_minority_count = min(
                class_counts.get(c, 0) for c in [1, 2, 3, 4] if class_counts.get(c, 0) > 0
            )
            k = min(5, min_minority_count - 1)
            k = max(k, 1)

            majority_n = class_counts.get(0, 1)
            target = max(int(majority_n * 0.15), min_minority_count)

            sampling_strategy = {}
            for c in [1, 2, 3, 4]:
                if class_counts.get(c, 0) > 0:
                    sampling_strategy[c] = max(target, class_counts.get(c, 0))

            smote = SMOTE(
                sampling_strategy=sampling_strategy,
                k_neighbors=k,
                random_state=42
            )
            X_res, y_res = smote.fit_resample(X_sm, y_sm)

            df_res = pd.DataFrame(X_res, columns=smote_cols)

            # Discretizar InputX/InputZ sintéticos al valor original más cercano {-1, 0, 1}
            for col in movement_cols:
                df_res[col] = df_res[col].apply(
                    lambda v: 1.0 if v > 0.5 else (-1.0 if v < -0.5 else 0.0)
                )

            df_res['Disparo'] = (y_res == 1).astype(int)
            df_res['Pase']    = (y_res == 2).astype(int)
            df_res['RoboK']   = (y_res == 3).astype(int)
            df_res['RoboL']   = (y_res == 4).astype(int)

            raw = df_res
            print(f"\n  SMOTE aplicado (k_neighbors={k}, target={target}):")
            print(f"  Dataset total tras SMOTE: {len(raw)}")
            print(f"  Disparo=1: {int(raw['Disparo'].sum())} ({raw['Disparo'].mean()*100:.1f}%)")
            print(f"  Pase=1:    {int(raw['Pase'].sum())} ({raw['Pase'].mean()*100:.1f}%)")
            print(f"  RoboK=1:   {int(raw['RoboK'].sum())} ({raw['RoboK'].mean()*100:.1f}%)")
            print(f"  RoboL=1:   {int(raw['RoboL'].sum())} ({raw['RoboL'].mean()*100:.1f}%)")
        else:
            print("\n  WARNING: No hay frames de Disparo/Pase con pelota en los datos!")

        if '_smote_class' in raw.columns:
            raw.drop('_smote_class', axis=1, inplace=True)

        # -- Generar las 9 clases de Movimiento --
        # Mapeo:
        # dx en {-1, 0, 1} -> dx + 1 en {0, 1, 2}
        # dz en {-1, 0, 1} -> dz + 1 en {0, 1, 2}
        # clase = (dx + 1) * 3 + (dz + 1)
        # 0: Diag Izq-Abj, 1: Izquierda, 2: Diag Izq-Arr
        # 3: Abajo,        4: Quieto,    5: Arriba
        # 6: Diag Der-Abj, 7: Derecha,   8: Diag Der-Arr
        def get_movement_class(row):
            dx = 1.0 if row['InputX'] > 0.5 else (-1.0 if row['InputX'] < -0.5 else 0.0)
            dz = 1.0 if row['InputZ'] > 0.5 else (-1.0 if row['InputZ'] < -0.5 else 0.0)
            return int((dx + 1) * 3 + (dz + 1))
            
        raw['MovementClass'] = raw.apply(get_movement_class, axis=1)

        self.data = raw.sample(frac=1, random_state=42).reset_index(drop=True)
        print(f"\n  Filas finales del dataset: {len(self.data)}")

        self.X  = torch.tensor(self.data[feature_cols].values,  dtype=torch.float32)
        self.Ym = torch.tensor(self.data['MovementClass'].values, dtype=torch.long)
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
def binarize(values, threshold=0.5):
    """Convierte salida sigmoide a clase binaria {0, 1}."""
    return (values >= threshold).astype(int)


# ============================================================================
# 5. ENTRENAMIENTO
# ============================================================================
def train(timestamp='', csv_files=None, onnx_output_path=None, scaler_output=None):
    """
    Entrena el modelo FNN de clasificación.
    Si no se pasan parámetros, usa los valores por defecto (constantes globales).
    """
    if csv_files is None:
        csv_files = CSV_FILES
    if onnx_output_path is None:
        onnx_output_path = ONNX_OUTPUT_PATH
    if scaler_output is None:
        scaler_output = SCALER_OUTPUT

    print("=" * 60)
    print("  ENTRENANDO MODELO CLASIFICACION FNN")
    print("=" * 60)

    print("\nCargando datasets...")
    dataset = SoccerDataset(csv_files)

    # Split 80 / 20
    indices = np.arange(len(dataset))
    train_idx, test_idx = train_test_split(indices, test_size=0.2, random_state=42)
    train_loader = DataLoader(Subset(dataset, train_idx), batch_size=BATCH_SIZE, shuffle=True)
    test_loader  = DataLoader(Subset(dataset, test_idx),  batch_size=BATCH_SIZE, shuffle=False)
    print(f"\nSplit: {len(train_idx)} train | {len(test_idx)} test")

    # Guardar scaler para Unity
    with open(scaler_output, "w") as f:
        json.dump({"mean": dataset.mean.squeeze().tolist(),
                   "std":  dataset.std.squeeze().tolist()}, f)
    print(f"Scaler guardado en {scaler_output}")

    INPUT_SIZE = 40
    model = SoccerAgentModel(INPUT_SIZE)
    
    # Loss para movimiento (9 clases)
    ce_crit = nn.CrossEntropyLoss()

    # Calcular pos_weight para compensar el desbalance extremo de acciones
    action_counts = dataset.Ya.sum(dim=0)  # [shoot_count, pass_count, robok_count, robol_count]
    total_samples = len(dataset)
    pos_weight = torch.zeros(4)
    for i, name in enumerate(['Disparo', 'Pase', 'RoboK', 'RoboL']):
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
        total_loss = total_ce = total_bce = 0.0

        for batch_X, batch_Ym, batch_Ya in train_loader:
            optimizer.zero_grad()

            pred_movement, pred_actions = model(batch_X)

            loss_ce  = ce_crit(pred_movement, batch_Ym)
            loss_bce = bce_crit(pred_actions,  batch_Ya)
            loss = MOVEMENT_LOSS_WEIGHT * loss_ce + ACTION_LOSS_WEIGHT * loss_bce

            loss.backward()
            optimizer.step()

            total_loss += loss.item()
            total_ce   += loss_ce.item()
            total_bce  += loss_bce.item()

        if (epoch + 1) % 10 == 0:
            n = len(train_loader)
            print(f"  Epoch [{epoch+1:3d}/{EPOCHS}] "
                  f"Loss={total_loss/n:.4f}  "
                  f"CE(mov)={total_ce/n:.4f}  "
                  f"BCE(actions)={total_bce/n:.4f}")

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
            # pm es [B, 9] (logits), sacamos la clase con argmax
            all_pred_mov.append(torch.argmax(pm, dim=1).cpu().numpy())
            all_pred_act.append(torch.sigmoid(pa).cpu().numpy())  # logits -> probabilidades
            all_true_mov.append(batch_Ym.cpu().numpy())
            all_true_act.append(batch_Ya.cpu().numpy())

    pred_mov = np.concatenate(all_pred_mov)
    true_mov = np.concatenate(all_true_mov)
    pred_act = np.vstack(all_pred_act)
    true_act = np.vstack(all_true_act)

    # -- Movimiento (9 clases) --
    print("\n[Movimiento — 9 Clases]")
    target_names = [
        "Diag Izq-Abj(0)", "Izquierda(1)", "Diag Izq-Arr(2)",
        "Abajo(3)", "Quieto(4)", "Arriba(5)",
        "Diag Der-Abj(6)", "Derecha(7)", "Diag Der-Arr(8)"
    ]
    print(classification_report(true_mov, pred_mov, labels=list(range(9)),
                                target_names=target_names,
                                zero_division=0))

    acc_mov  = accuracy_score(true_mov, pred_mov)
    prec_mov = precision_score(true_mov, pred_mov, average='macro', zero_division=0)
    rec_mov  = recall_score(true_mov, pred_mov, average='macro', zero_division=0)
    f1_mov   = f1_score(true_mov, pred_mov, average='macro', zero_division=0)
    print(f"[Movimiento] Accuracy={acc_mov*100:.1f}%  Prec={prec_mov*100:.1f}%  Rec={rec_mov*100:.1f}%  F1={f1_mov*100:.1f}%")

    # -- Acciones binarias --
    pred_shoot = binarize(pred_act[:, 0]);  true_shoot = true_act[:, 0].astype(int)
    pred_pass  = binarize(pred_act[:, 1]);  true_pass  = true_act[:, 1].astype(int)
    pred_robok = binarize(pred_act[:, 2]);  true_robok = true_act[:, 2].astype(int)
    pred_robol = binarize(pred_act[:, 3]);  true_robol = true_act[:, 3].astype(int)

    print("\n[Disparo]")
    print(classification_report(true_shoot, pred_shoot, labels=[0,1],
                                target_names=["No chuta","Chuta"],
                                zero_division=0))

    print("[Pase]")
    print(classification_report(true_pass, pred_pass, labels=[0,1],
                                target_names=["No pasa","Pasa"],
                                zero_division=0))

    print("[RoboK]")
    print(classification_report(true_robok, pred_robok, labels=[0,1],
                                target_names=["No robaK","RobaK"],
                                zero_division=0))

    print("[RoboL]")
    print(classification_report(true_robol, pred_robol, labels=[0,1],
                                target_names=["No robaL","RobaL"],
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
    acc_robok  = accuracy_score(true_robok, pred_robok)
    prec_robok = precision_score(true_robok, pred_robok, average='binary', zero_division=0)
    rec_robok  = recall_score(true_robok, pred_robok, average='binary', zero_division=0)
    f1_robok   = f1_score(true_robok, pred_robok, average='binary', zero_division=0)
    acc_robol  = accuracy_score(true_robol, pred_robol)
    prec_robol = precision_score(true_robol, pred_robol, average='binary', zero_division=0)
    rec_robol  = recall_score(true_robol, pred_robol, average='binary', zero_division=0)
    f1_robol   = f1_score(true_robol, pred_robol, average='binary', zero_division=0)

    print("[METRICS_START]")
    print(f"MODEL=ClasificacionFNN")
    print(f"TIMESTAMP={timestamp}")
    print(f"ACC_MOV={acc_mov*100:.2f}")
    print(f"PREC_MOV={prec_mov*100:.2f}")
    print(f"REC_MOV={rec_mov*100:.2f}")
    print(f"F1_MOV={f1_mov*100:.2f}")
    print(f"ACC_SHOOT={acc_shoot*100:.2f}")
    print(f"PREC_SHOOT={prec_shoot*100:.2f}")
    print(f"REC_SHOOT={rec_shoot*100:.2f}")
    print(f"F1_SHOOT={f1_shoot*100:.2f}")
    print(f"ACC_PASS={acc_pass*100:.2f}")
    print(f"PREC_PASS={prec_pass*100:.2f}")
    print(f"REC_PASS={rec_pass*100:.2f}")
    print(f"F1_PASS={f1_pass*100:.2f}")
    print(f"ACC_ROBOK={acc_robok*100:.2f}")
    print(f"PREC_ROBOK={prec_robok*100:.2f}")
    print(f"REC_ROBOK={rec_robok*100:.2f}")
    print(f"F1_ROBOK={f1_robok*100:.2f}")
    print(f"ACC_ROBOL={acc_robol*100:.2f}")
    print(f"PREC_ROBOL={prec_robol*100:.2f}")
    print(f"REC_ROBOL={rec_robol*100:.2f}")
    print(f"F1_ROBOL={f1_robol*100:.2f}")
    print(f"LOSS_FINAL={last_loss:.4f}")
    print("[METRICS_END]")

    # ── EXPORTAR ONNX ──
    # Wrapper que aplica softmax a los logits de movimiento y sigmoid a acciones
    class ExportWrapper(nn.Module):
        def __init__(self, base_model):
            super().__init__()
            self.base = base_model
        def forward(self, x):
            movement_logits, action_logits = self.base(x)
            return torch.softmax(movement_logits, dim=-1), torch.sigmoid(action_logits)

    export_model = ExportWrapper(model)
    export_model.eval()
    dummy = torch.randn(1, INPUT_SIZE)
    try:
        torch.onnx.export(
            export_model, dummy, onnx_output_path,
            export_params=True, opset_version=14,
            do_constant_folding=True,
            input_names=['vector_observation'],
            output_names=['movement_probs', 'action_probs'],
            dynamic_axes={
                'vector_observation':  {0: 'batch_size'},
                'movement_probs':      {0: 'batch_size'},
                'action_probs':        {0: 'batch_size'},
            },
            dynamo=False
        )
        print(f"Modelo ONNX guardado en {onnx_output_path}")
    except Exception as e:
        print(f"ONNX export fallo ({e}), guardando como .pt...")
        traced = torch.jit.trace(export_model, dummy)
        pt_path = onnx_output_path.replace('.onnx', '.pt')
        traced.save(pt_path)
        print(f"Modelo JIT guardado en {pt_path}")

    print("\n¡Entrenamiento Clasificacion FNN completado!")

    # Devolver resultados para el servidor
    return {
        'onnx_path': onnx_output_path,
        'scaler_path': scaler_output,
        'metrics': {
            'acc_mov': acc_mov, 'prec_mov': prec_mov,
            'rec_mov': rec_mov, 'f1_mov': f1_mov,
            'acc_shoot': acc_shoot, 'prec_shoot': prec_shoot,
            'rec_shoot': rec_shoot, 'f1_shoot': f1_shoot,
            'acc_pass': acc_pass, 'prec_pass': prec_pass,
            'rec_pass': rec_pass, 'f1_pass': f1_pass,
            'acc_robok': acc_robok, 'prec_robok': prec_robok,
            'rec_robok': rec_robok, 'f1_robok': f1_robok,
            'acc_robol': acc_robol, 'prec_robol': prec_robol,
            'rec_robol': rec_robol, 'f1_robol': f1_robol,
            'loss_final': last_loss,
        }
    }


if __name__ == "__main__":
    ts  = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
    log_dir  = os.path.join(os.path.dirname(__file__) or '.', 'logs')
    log_path = os.path.join(log_dir, f'train_clasificacion_fnn_{ts}.txt')
    tee = Tee(log_path)
    sys.stdout = tee
    try:
        train(timestamp=ts)
    finally:
        tee.close()
