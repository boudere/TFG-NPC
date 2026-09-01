"""
Entrena el MISMO FNN sobre la MISMA particion en cuatro condiciones:
  A  sin SMOTE, con pos_weight        (lo que hay ahora)
  B  SMOTE bien puesto: solo en train, despues de partir, + pos_weight
  C  SMOTE en lugar de pos_weight     (por si se estaba corrigiendo dos veces)
  D  SMOTE mal puesto: antes de partir, particion aleatoria (el pipeline viejo)
A, B y C comparten conjunto de test, asi que son comparables entre si.
D no lo es: su test contiene muestras derivadas de su propio entrenamiento.
Se incluye solo para ver que numero se habria publicado.
"""
import numpy as np, pandas as pd, torch, torch.nn as nn
from torch.utils.data import TensorDataset, DataLoader
from imblearn.over_sampling import SMOTE
from sklearn.metrics import f1_score, accuracy_score
import comun
from comun import FEATURES, ACCIONES
from arquitecturas import FNNDualHead

EPOCAS, LR, BATCH, PESO_ACCION = 150, 1e-3, 32, 2.0
MOV = ['InputX', 'InputZ']


def smote(df):
    """SMOTE tal y como lo hacia el script original del proyecto."""
    cls = np.zeros(len(df), int)
    cls[df['Pase'].values == 1] = 2
    cls[df['Disparo'].values == 1] = 1
    cnt = pd.Series(cls).value_counts()
    obj = {c: max(int(cnt[0] * 0.15), int(cnt[c])) for c in [1, 2] if c in cnt and cnt[c] >= 2}
    if not obj:
        return df
    Xr, yr = SMOTE(sampling_strategy=obj, k_neighbors=5, random_state=42
                   ).fit_resample(df[FEATURES + MOV].values, cls)
    out = pd.DataFrame(Xr, columns=FEATURES + MOV)
    # el script original re-discretizaba a mano: senyal de que lo sintetico no era valido
    for c in MOV:
        out[c] = out[c].apply(lambda v: 1.0 if v > 0.5 else (-1.0 if v < -0.5 else 0.0))
    out['Disparo'] = (yr == 1).astype(int)
    out['Pase'] = (yr == 2).astype(int)
    dx = np.where(out['InputX'] > 0.5, 1, np.where(out['InputX'] < -0.5, -1, 0))
    dz = np.where(out['InputZ'] > 0.5, 1, np.where(out['InputZ'] < -0.5, -1, 0))
    out['_mov'] = (dx + 1) * 3 + (dz + 1)
    out['_puerta'] = (out['TienePelota'] > 0.5).astype(int)
    return out


def entrenar(Xtr, ymtr, yatr, usar_pw):
    comun.fijar_semilla()
    m = FNNDualHead(40)
    opt = torch.optim.Adam(m.parameters(), lr=LR)
    if usar_pw:
        w = [(len(yatr) - yatr[:, j].sum()) / max(yatr[:, j].sum(), 1) for j in range(2)]
        pw = torch.tensor(np.clip(w, 1.0, 50.0), dtype=torch.float32)
    else:
        pw = torch.ones(2)
    ce, bce = nn.CrossEntropyLoss(), nn.BCEWithLogitsLoss(pos_weight=pw)
    dl = DataLoader(TensorDataset(torch.tensor(Xtr), torch.tensor(ymtr), torch.tensor(yatr)),
                    batch_size=BATCH, shuffle=True)
    m.train()
    for _ in range(EPOCAS):
        for xb, mb, ab in dl:
            opt.zero_grad()
            pm, pa = m(xb)
            (ce(pm, mb) + PESO_ACCION * bce(pa, ab)).backward()
            opt.step()
    return m


@torch.no_grad()
def medir(m, X, ym, ya, g):
    m.eval()
    lm, la = m(torch.tensor(X))
    pm = lm.numpy().argmax(1)
    pa = 1 / (1 + np.exp(-np.clip(la.numpy(), -50, 50)))
    r = {'acc_mov': accuracy_score(ym, pm) * 100,
         'f1_mov': f1_score(ym, pm, average='macro', zero_division=0) * 100}
    for j, c in enumerate(ACCIONES):
        if g.sum() == 0 or ya[g, j].sum() == 0:
            r[f'f1_{c}'] = float('nan'); continue
        r[f'f1_{c}'] = f1_score(ya[g, j].astype(int), (pa[g, j] >= .5).astype(int),
                                zero_division=0) * 100
    return r


def arrays(df, mean, std):
    X = ((df[FEATURES].values.astype(np.float32) - mean) / std)
    return (X, df['_mov'].values.astype(np.int64),
            df[ACCIONES].values.astype(np.float32), df['_puerta'].values.astype(bool))


ses = comun.cargar_sesiones(['datos/SoccerData_*.csv'])
train, test = comun.dividir(ses)
dtr, dte = pd.concat(train, ignore_index=True), pd.concat(test, ignore_index=True)
mean, std = comun.escalador(train)
Xte, ymte, yate, gte = arrays(dte, mean, std)
print(f"\ntrain {len(dtr)} filas | test {len(dte)} filas (2 sesiones sin ver)\n")

res = {}
Xtr, ymtr, yatr, _ = arrays(dtr, mean, std)
print("A  sin SMOTE, con pos_weight ...")
res['A · sin SMOTE (actual)'] = medir(entrenar(Xtr, ymtr, yatr, True), Xte, ymte, yate, gte)

dsm = smote(dtr)
Xs, yms, yas, _ = arrays(dsm, mean, std)
print(f"B  SMOTE solo en train ({len(dtr)} -> {len(dsm)} filas), con pos_weight ...")
res['B · SMOTE correcto + pos_weight'] = medir(entrenar(Xs, yms, yas, True), Xte, ymte, yate, gte)
print("C  SMOTE solo en train, SIN pos_weight ...")
res['C · SMOTE correcto, sin pos_weight'] = medir(entrenar(Xs, yms, yas, False), Xte, ymte, yate, gte)

print("D  SMOTE antes de partir + particion aleatoria (pipeline viejo) ...")
todo = smote(pd.concat(ses, ignore_index=True))
rng = np.random.default_rng(42)
perm = rng.permutation(len(todo)); corte = int(len(todo) * .8)
d1, d2 = todo.iloc[perm[:corte]], todo.iloc[perm[corte:]]
m1 = d1[FEATURES].values.astype(np.float32).mean(0)
s1 = d1[FEATURES].values.astype(np.float32).std(0); s1[s1 < 1e-6] = 1
X1, ym1, ya1, _ = arrays(d1, m1, s1)
X2, ym2, ya2, g2 = arrays(d2, m1, s1)
res['D · SMOTE antes de partir (viejo)'] = medir(entrenar(X1, ym1, ya1, True), X2, ym2, ya2, g2)

print("\n" + "=" * 82)
print(f"{'condicion':38} {'Acc mov':>8} {'F1 mov':>8} {'F1 disparo':>11} {'F1 pase':>9}")
print("=" * 82)
for k, v in res.items():
    print(f"{k:38} {v['acc_mov']:8.1f} {v['f1_mov']:8.1f} "
          f"{v['f1_Disparo']:11.1f} {v['f1_Pase']:9.1f}")
print("\nA, B y C comparten test. D tiene su propio test contaminado: no es comparable.")
