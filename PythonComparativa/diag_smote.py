"""Diagnostico: cuanto desbalance queda de verdad, y que fabrica SMOTE."""
import numpy as np, pandas as pd
from sklearn.neighbors import NearestNeighbors
import comun
from comun import FEATURES, ACCIONES

ses = comun.cargar_sesiones(['datos/SoccerData_*.csv'])
train, test = comun.dividir(ses)
raw = pd.concat(train, ignore_index=True)

print("\n" + "="*78)
print("1. CUANTO DESBALANCE QUEDA REALMENTE")
print("="*78)
g = raw['_puerta'].values.astype(bool)
print(f"{'accion':10} {'% sobre TODOS los frames':>25} {'% DENTRO de su puerta':>24}")
for c in ACCIONES:
    todo = raw[c].mean()*100
    dentro = raw[c].values[g].mean()*100
    print(f"{c:10} {todo:24.2f}% {dentro:23.1f}%")
print(f"\n  frames con balon: {g.sum()} de {len(raw)} ({g.mean()*100:.1f}%)")
print("  -> El desbalance que motivaba SMOTE era en gran parte artificial: venia de")
print("     evaluar 'deberias pasar?' en frames donde pasar es imposible.")

print("\n" + "="*78)
print("2. QUE VECINOS USA SMOTE")
print("="*78)
# Vecinos de los positivos de Pase dentro del propio dataset
X = raw[FEATURES].values.astype(np.float32)
X = (X - X.mean(0)) / np.where(X.std(0) < 1e-6, 1, X.std(0))
idx_pos = np.flatnonzero(raw['Pase'].values == 1)
if len(idx_pos) > 5:
    nn = NearestNeighbors(n_neighbors=6).fit(X[idx_pos])
    _, vec = nn.kneighbors(X[idx_pos])
    # distancia TEMPORAL (en frames) entre cada positivo y sus 5 vecinos
    sesion = raw['_sesion'].values
    dt, misma = [], 0
    for i, fila in enumerate(vec):
        a = idx_pos[i]
        for j in fila[1:]:
            b = idx_pos[j]
            if sesion[a] == sesion[b]:
                dt.append(abs(int(a) - int(b))); misma += 1
    dt = np.array(dt)
    print(f"  positivos de Pase analizados: {len(idx_pos)}")
    print(f"  vecinos en la MISMA sesion: {misma} de {len(idx_pos)*5}")
    print(f"  separacion temporal mediana: {np.median(dt):.0f} frames = {np.median(dt)*0.1:.1f} s")
    print(f"  vecinos a <= 5 frames (0.5 s): {(dt<=5).mean()*100:.0f}%")
    print(f"  vecinos a <= 10 frames (1 s):  {(dt<=10).mean()*100:.0f}%")
    print("  -> SMOTE interpola entre fotogramas casi consecutivos: no crea")
    print("     diversidad nueva, rellena entre estados casi identicos.")

print("\n" + "="*78)
print("3. SON VALIDAS LAS MUESTRAS SINTETICAS?")
print("="*78)
from imblearn.over_sampling import SMOTE
mov = ['InputX', 'InputZ']
cls = np.zeros(len(raw), int)
cls[raw['Pase'].values == 1] = 2
cls[raw['Disparo'].values == 1] = 1
cnt = pd.Series(cls).value_counts()
objetivo = {c: max(int(cnt[0]*0.15), int(cnt[c])) for c in [1, 2] if c in cnt and cnt[c] >= 2}
sm = SMOTE(sampling_strategy=objetivo, k_neighbors=5, random_state=42)
Xr, yr = sm.fit_resample(raw[FEATURES + mov].values, cls)
n_orig = len(raw); n_new = len(Xr) - n_orig
print(f"  muestras originales {n_orig}, sinteticas anyadidas {n_new}")
df = pd.DataFrame(Xr[n_orig:], columns=FEATURES + mov)
binarias = {'TienePelota': [0, 1], 'TienePelotaEquipo': [0, 1, 2],
            'PuntuacionPropia': None, 'PuntuacionContraria': None}
for col, validos in binarias.items():
    v = df[col].values
    if validos is None:
        malo = (np.abs(v - np.round(v)) > 1e-6).mean()
    else:
        malo = (~np.isin(np.round(v, 6), validos)).mean()
    print(f"  {col:20} {malo*100:5.1f}% de las sinteticas toman un valor imposible")
mx = df[mov].values
fuera = (np.abs(np.abs(mx) - np.round(np.abs(mx))) > 1e-6).any(1).mean()
print(f"  {'InputX/InputZ':20} {fuera*100:5.1f}% caen fuera de {{-1,0,1}} "
      f"(por eso el script original los tenia que re-discretizar a mano)")
