import pandas as pd
import numpy as np

path = '../Assets/SoccerData_Temporal_2026_05_05_18_15_22.csv'
df = pd.read_csv(path)
mov = np.round(df[['InputX', 'InputZ']].values)
classes = (mov[:, 0] + 1) * 3 + (mov[:, 1] + 1)
counts = pd.Series(classes).value_counts()
print(counts.sort_index())
