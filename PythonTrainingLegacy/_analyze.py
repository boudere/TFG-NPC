import pandas as pd
import glob

files = glob.glob('../Assets/SoccerData_*.csv')
df = pd.concat([pd.read_csv(f) for f in files], ignore_index=True)

print(f"Total frames: {len(df)}")
print(f"TienePelota=1: {int(df['TienePelota'].sum())} ({df['TienePelota'].mean()*100:.1f}%)")
print(f"TienePelota=0: {int((df['TienePelota']==0).sum())} ({(df['TienePelota']==0).mean()*100:.1f}%)")

# Frames alrededor de cada disparo
print("\n=== CONTEXTO TEMPORAL DE DISPAROS ===")
shoot_indices = df.index[df['Disparo'] == 1].tolist()
print(f"Indices de disparos: {len(shoot_indices)}")

for idx in shoot_indices[:10]:  # Primeros 10
    start = max(0, idx - 3)
    end = min(len(df) - 1, idx + 2)
    window = df.loc[start:end, ['TienePelota', 'TienePelotaEquipo', 'DistPelota', 'Disparo', 'Pase']]
    marker = []
    for i in window.index:
        marker.append("<<< DISPARO" if i == idx else "")
    window = window.copy()
    window[''] = marker
    print(f"\n--- Frame {idx} ---")
    print(window.to_string())

# Lo mismo para pases
print("\n\n=== CONTEXTO TEMPORAL DE PASES ===")
pass_indices = df.index[df['Pase'] == 1].tolist()
for idx in pass_indices[:10]:
    start = max(0, idx - 3)
    end = min(len(df) - 1, idx + 2)
    window = df.loc[start:end, ['TienePelota', 'TienePelotaEquipo', 'DistPelota', 'Disparo', 'Pase']]
    marker = []
    for i in window.index:
        marker.append("<<< PASE" if i == idx else "")
    window = window.copy()
    window[''] = marker
    print(f"\n--- Frame {idx} ---")
    print(window.to_string())
