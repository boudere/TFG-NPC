"""
Script minimo para re-entrenar y exportar el modelo Sliding Window.
Usa el mismo codigo que train_sliding.py pero al final exporta con la API 
correcta.
"""
import sys, os, io
# Forzar UTF-8 en la consola de Windows para evitar crashes con emojis de torch.onnx
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace')
# Asegurar que onnxscript se encuentre si fue instalado localmente
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '_libs'))
os.environ['PYTHONIOENCODING'] = 'utf-8'

import torch
import torch.nn as nn
import numpy as np
import pandas as pd
import glob, json
from torch.utils.data import Dataset, DataLoader, Subset
from sklearn.model_selection import train_test_split
from imblearn.over_sampling import SMOTE

# ---------- PARAMETROS ----------
CSV_FILES = glob.glob('../Assets/SoccerData_Temporal_2026_05_05_18_15_22.csv')
ONNX_OUTPUT_PATH = '../Assets/SoccerModel_Sliding.onnx'
SCALER_OUTPUT    = '../Assets/scaler_sliding.json'
EPOCHS       = 150
BATCH_SIZE   = 32
LR           = 0.001
MOV_W        = 1.0
ACT_W        = 2.0

# ---------- MODELO ----------
class SoccerSlidingAgentModel(nn.Module):
    def __init__(self, input_size: int):
        super().__init__()
        self.movement_branch = nn.Sequential(
            nn.Linear(input_size, 128), nn.ReLU(),
            nn.Linear(128, 64), nn.ReLU(),
            nn.Linear(64, 9)
        )
        self.action_branch = nn.Sequential(
            nn.Linear(input_size, 128), nn.ReLU(),
            nn.Linear(128, 64), nn.ReLU(),
            nn.Linear(64, 2)
        )
    def forward(self, x):
        return self.movement_branch(x), self.action_branch(x)

# ---------- DATASET ----------
class SlidingDS(Dataset):
    def __init__(self, csv_files):
        feature_cols = [
            'RelPorteriaRivalX','RelPorteriaRivalZ',
            'RelPorteriaPropiaX','RelPorteriaPropiaZ',
            'TienePelota','RelPelotaX','RelPelotaZ','DistPelota','TienePelotaEquipo',
            'DistPorteriaContraria','DistPorteriaPropia','PuntuacionPropia','PuntuacionContraria',
            'DistPelotaPorteriaPropia','DistAliadoCercano','DistEnemigoCercano',
            'RelAliado1PosX','RelAliado1PosZ','Aliado1DirX','Aliado1DirZ',
            'RelAliado2PosX','RelAliado2PosZ','Aliado2DirX','Aliado2DirZ',
            'RelAliado3PosX','RelAliado3PosZ','Aliado3DirX','Aliado3DirZ',
            'RelEnemigo1PosX','RelEnemigo1PosZ','Enemigo1DirX','Enemigo1DirZ',
            'RelEnemigo2PosX','RelEnemigo2PosZ','Enemigo2DirX','Enemigo2DirZ',
            'RelEnemigo3PosX','RelEnemigo3PosZ','Enemigo3DirX','Enemigo3DirZ',
        ]
        movement_cols = ['InputX','InputZ']
        action_cols   = ['Disparo','Pase']
        frames = []
        for path in csv_files:
            if not os.path.exists(path): continue
            df_full = pd.read_csv(path)
            if 'EpisodeID' not in df_full.columns:
                df_full['EpisodeID'] = 0
            for _, tmp in df_full.groupby('EpisodeID'):
                tmp = tmp.reset_index(drop=True)
                LOOKBACK = 5
                for ac in ['Disparo','Pase']:
                    for idx in tmp.index[tmp[ac]==1].tolist():
                        if tmp.loc[idx,'TienePelota']==1: continue
                        found=False
                        for off in range(1,LOOKBACK+1):
                            pi=idx-off
                            if pi<0: break
                            if tmp.loc[pi,'TienePelota']==1:
                                tmp.loc[idx,ac]=0; tmp.loc[pi,ac]=1; found=True; break
                        if not found: tmp.loc[idx,ac]=0
                prev = tmp[feature_cols].shift(1).add_suffix('_prev')
                comb = pd.concat([prev, tmp], axis=1)
                spawn = (tmp['Aliado1DirX']==0)&(tmp['Aliado1DirZ']==0)&(tmp['Aliado2DirX']==0)&(tmp['Aliado2DirZ']==0)
                valid = (~spawn)&(prev[feature_cols[0]+'_prev'].notna())
                frames.append(comb[valid])
        if not frames: raise FileNotFoundError("No CSV")
        raw = pd.concat(frames, ignore_index=True)
        fc = [c+'_prev' for c in feature_cols]+feature_cols

        X_raw = torch.tensor(raw[fc].values, dtype=torch.float32)
        self.mean = X_raw.mean(dim=0, keepdim=True)
        self.std  = X_raw.std(dim=0, keepdim=True); self.std[self.std==0]=1.0

        raw['_sc']=0; raw.loc[raw['Pase']==1,'_sc']=2; raw.loc[raw['Disparo']==1,'_sc']=1
        sc = raw[fc+movement_cols].values; y_sc = raw['_sc'].values
        cc = pd.Series(y_sc).value_counts().sort_index()
        nmin = sum(cc.get(c,0) for c in [1,2])
        if nmin>0:
            mnc = min(cc.get(c,0) for c in [1,2] if cc.get(c,0)>0)
            k = max(1,min(5,mnc-1))
            tgt = max(int(cc.get(0,1)*0.15), mnc)
            ss = {c:max(tgt,cc.get(c,0)) for c in [1,2] if cc.get(c,0)>0}
            n_orig = len(raw)
            Xr,yr = SMOTE(sampling_strategy=ss,k_neighbors=k,random_state=42).fit_resample(sc,y_sc)
            df2 = pd.DataFrame(Xr, columns=fc+movement_cols)
            df2['Disparo']=(yr==1).astype(int); df2['Pase']=(yr==2).astype(int)
            is_s=np.zeros(len(Xr),dtype=np.float32); is_s[n_orig:]=1.0; df2['is_synthetic']=is_s
            raw=df2
        else:
            raw['is_synthetic']=0.0
        if '_sc' in raw.columns: raw.drop('_sc',axis=1,inplace=True)
        self.data = raw.sample(frac=1,random_state=42).reset_index(drop=True)
        self.X = torch.tensor(self.data[fc].values, dtype=torch.float32)
        mv = np.round(self.data[movement_cols].values)
        self.Ym = torch.tensor((mv[:,0]+1)*3+(mv[:,1]+1), dtype=torch.long)
        self.Ya = torch.tensor(self.data[action_cols].values, dtype=torch.float32)
        self.IS = torch.tensor(self.data['is_synthetic'].values, dtype=torch.float32)
        self.X = (self.X - self.mean) / self.std
    def __len__(self): return len(self.data)
    def __getitem__(self, i): return self.X[i], self.Ym[i], self.Ya[i], self.IS[i]

# ---------- TRAIN + EXPORT ----------
def main():
    ds = SlidingDS(CSV_FILES)
    idx = np.arange(len(ds))
    tr_i, te_i = train_test_split(idx, test_size=0.2, random_state=42)
    tr_dl = DataLoader(Subset(ds,tr_i), batch_size=BATCH_SIZE, shuffle=True)

    with open(SCALER_OUTPUT,"w") as f:
        json.dump({"mean":ds.mean.squeeze().tolist(),"std":ds.std.squeeze().tolist()},f)

    model = SoccerSlidingAgentModel(80)
    ce = nn.CrossEntropyLoss()
    ac = ds.Ya.sum(dim=0); tot=len(ds)
    pw = torch.zeros(2)
    for i in range(2):
        p=max(ac[i].item(),1.0); pw[i]=min((tot-p)/p,50.0)
    bce = nn.BCEWithLogitsLoss(pos_weight=pw)
    opt = torch.optim.Adam(model.parameters(), lr=LR)

    for ep in range(EPOCHS):
        model.train()
        for bX,bYm,bYa,bIS in tr_dl:
            opt.zero_grad()
            pm,pa = model(bX)
            om = (bIS==0.0)
            lce = ce(pm[om],bYm[om]) if om.sum()>0 else torch.tensor(0.0)
            lbce = bce(pa,bYa)
            loss = MOV_W*lce + ACT_W*lbce
            if loss.item()>0: loss.backward(); opt.step()
        if (ep+1)%25==0 or ep==0:
            print(f"Epoch {ep+1}/{EPOCHS} | CE={lce.item():.4f} | BCE={lbce.item():.4f}")

    # -- Verificar predicciones antes de exportar --
    model.eval()
    with torch.no_grad():
        sample = ds.X[:10]
        mov_out, act_out = model(sample)
        preds = torch.argmax(mov_out, dim=1)
        print(f"\nMovement logits (primeras 3 muestras):")
        for i in range(3):
            print(f"  Sample {i}: {mov_out[i].numpy()} -> class {preds[i].item()}")
        print(f"Movement class distribution (10 muestras): {preds.numpy()}")

    # -- Exportar ONNX --
    class Wrapper(nn.Module):
        def __init__(self, m):
            super().__init__()
            self.m = m
        def forward(self, x):
            mov, act = self.m(x)
            return torch.softmax(mov, dim=-1), torch.sigmoid(act)

    wr = Wrapper(model); wr.eval()
    dummy = torch.randn(1, 80)
    
    torch.onnx.export(
        wr, dummy, ONNX_OUTPUT_PATH,
        export_params=True, opset_version=14,
        do_constant_folding=True,
        input_names=['vector_observation'],
        output_names=['continuous_actions', 'discrete_actions'],
        dynamo=False
    )
    print(f"\nONNX exportado en: {ONNX_OUTPUT_PATH}")

    # Verificar el ONNX exportado
    import onnxruntime as ort
    sess = ort.InferenceSession(ONNX_OUTPUT_PATH)
    print(f"ONNX inputs: {[i.name + ' ' + str(i.shape) for i in sess.get_inputs()]}")
    print(f"ONNX outputs: {[o.name + ' ' + str(o.shape) for o in sess.get_outputs()]}")
    
    test_input = ds.X[0:1].numpy()
    results = sess.run(None, {'vector_observation': test_input})
    print(f"Movement output shape: {results[0].shape}, values: {results[0]}")
    print(f"Actions output shape: {results[1].shape}, values: {results[1]}")

if __name__=="__main__":
    main()
