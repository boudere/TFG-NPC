import pandas as pd
import numpy as np

CSV = '../Assets/SoccerData_2026_03_31_20_20_59.csv'
df = pd.read_csv(CSV)

print('Filas:', len(df))
print('ActionShoot=1:', int(df['ActionShoot'].sum()), 'filas', f'({df["ActionShoot"].mean()*100:.1f}%)')
print('ActionPass=1: ', int(df['ActionPass'].sum()),  'filas', f'({df["ActionPass"].mean()*100:.1f}%)')

def bursts(col):
    b, p = 0, 0
    for v in col:
        if v == 1 and p == 0: b += 1
        p = v
    return b

print('Rafagas Shoot:', bursts(df['ActionShoot']))
print('Rafagas Pass: ', bursts(df['ActionPass']))

def disc(s, t=0.3):
    r = np.zeros(len(s), dtype=int)
    r[s > t] = 1
    r[s < -t] = -1
    return r

lx = {int(k): int(v) for k, v in pd.Series(disc(df['InputX'])).value_counts().sort_index().items()}
lz = {int(k): int(v) for k, v in pd.Series(disc(df['InputZ'])).value_counts().sort_index().items()}
print('InputX:', lx)
print('InputZ:', lz)
q = ((df['InputX'].abs() < 0.3) & (df['InputZ'].abs() < 0.3)).sum()
print(f'Quieto: {q} ({q/len(df)*100:.1f}%)  Moviendo: {len(df)-q} ({(len(df)-q)/len(df)*100:.1f}%)')

same = ((df['Ally1PosX'] == df['MyPosX']) & (df['Ally1PosZ'] == df['MyPosZ'])).sum()
print('Bug Ally1==MyPos:', same, '-> BUG' if same > 0 else '-> OK')

fc = [c for c in df.columns if c not in ['TotalTime','InputX','InputZ','ActionShoot','ActionPass']]
az = [c for c in fc if df[c].eq(0).all()]
print('Siempre a 0:', az if az else 'Ninguna')

n_shoot = int(df['ActionShoot'].sum())
n_pass  = int(df['ActionPass'].sum())
print('')
print('Shoot suficiente (>=50):', 'SI' if n_shoot >= 50 else f'NO — tiene {n_shoot}, necesita 50+')
print('Pass  suficiente (>=50):', 'SI' if n_pass  >= 50 else f'NO — tiene {n_pass}, necesita 50+')
