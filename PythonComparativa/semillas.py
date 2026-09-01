"""
Cuanto de la tabla 3-2 es senyal y cuanto es ruido.
Reentrena las dos candidatas principales con varias semillas sobre la MISMA
particion. Si la diferencia entre ellas sobrevive al cambio de semilla, es real.
"""
import numpy as np, pandas as pd, torch
import comun
from comun import FEATURES, ACCIONES
from arquitecturas import FNNDualHead, FNNSlidingWindow
import comparar as C

SEMILLAS = [0, 1, 2, 42]
ses = comun.cargar_sesiones(['datos/SoccerData_*.csv'])
train, test = comun.dividir(ses)
mean, std = comun.escalador(train)

Xtr, ymtr, yatr, _ = C._mat(train, mean, std), *C._etq(train)
Xte, ymte, yate, gte = C._mat(test, mean, std), *C._etq(test)
Xva, ymva, yava, _ = C._ventana(train, mean, std)
Xvb, ymvb, yavb, gvb = C._ventana(test, mean, std)
print(f"\ntest FNN: {len(Xte)} filas | test ventana: {len(Xvb)} filas\n")

filas = []
for s in SEMILLAS:
    for nombre, mk, dat, ev in [
        ('FNN dual-head', lambda: FNNDualHead(40), (Xtr, ymtr, yatr), (Xte, ymte, yate, gte)),
        ('FNN sliding window', lambda: FNNSlidingWindow(80), (Xva, ymva, yava), (Xvb, ymvb, yavb, gvb)),
    ]:
        comun.fijar_semilla(s)
        m = mk()
        C.entrenar_dual(m, *dat, 150, C._pos_weight(dat[2]))
        lm, la = C.predecir(m, ev[0])
        r = comun.evaluar(ev[1], lm.argmax(1), ev[2], C._sig(la), ev[3])
        r.update(semilla=s, arq=nombre)
        filas.append(r)
        print(f"  semilla {s:2}  {nombre:20} acc {r['acc_mov']:5.1f}  f1mov {r['f1_mov']:5.1f}  "
              f"disp {r['f1_disparo']:5.1f}  pase {r['f1_pase']:5.1f}")

df = pd.DataFrame(filas)
print("\n" + "=" * 78)
print("VARIABILIDAD ENTRE SEMILLAS (misma particion, mismos datos)")
print("=" * 78)
for col in ['acc_mov', 'f1_mov', 'f1_disparo', 'f1_pase']:
    print(f"\n{col}")
    for a, g in df.groupby('arq'):
        v = g[col].values
        print(f"  {a:20} media {v.mean():5.1f}  min {v.min():5.1f}  max {v.max():5.1f}  "
              f"rango {v.max()-v.min():4.1f}  desv {v.std(ddof=1):4.1f}")
df.to_csv('variabilidad_semillas.csv', index=False)
