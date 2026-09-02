"""
caracterizar_datos.py - Cifras del dataset para la memoria
==========================================================
Genera los numeros que la seccion "Ingenieria de Datos" debe citar, en vez de
estimaciones. Mide cuatro cosas:

  1. Cuantas sesiones tienen el esquema actual y cuantas se descartan.
  2. Cuanto pesa el ruido de reinicio (mascara "spawn": aliados con direccion
     nula, es decir, todos congelados tras un gol).
  3. Cuantas etiquetas de accion caen en un fotograma sin balon, cuantas se
     recolocan y cuantas se pierden.
  4. Cuanto del desbalance era artificial: prevalencia de cada accion sobre
     TODOS los fotogramas frente a la prevalencia DENTRO de su puerta.

Uso:
    python caracterizar_datos.py                       # usa datos/SoccerData_*.csv
    python caracterizar_datos.py ../Assets/SoccerData_*.csv
"""
import glob
import os
import sys

import numpy as np
import pandas as pd

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
try:
    from comun import FEATURES, ACCIONES, LOOKBACK, VENTANA_INTENCION, DT, N_MOV
except ImportError:                                    # ejecutado fuera de la carpeta
    ACCIONES, LOOKBACK, VENTANA_INTENCION, DT, N_MOV = ['Disparo', 'Pase'], 5, 5, 0.1, 9
    FEATURES = ['TienePelota', 'InputX', 'InputZ', 'Aliado1DirX', 'Aliado1DirZ',
                'Aliado2DirX', 'Aliado2DirZ']

MASCARA_SPAWN = ['Aliado1DirX', 'Aliado1DirZ', 'Aliado2DirX', 'Aliado2DirZ']


def clase_mov(d):
    dx = np.where(d['InputX'] > 0.5, 1, np.where(d['InputX'] < -0.5, -1, 0))
    dz = np.where(d['InputZ'] > 0.5, 1, np.where(d['InputZ'] < -0.5, -1, 0))
    return (dx + 1) * 3 + (dz + 1)


def main(patrones):
    rutas = []
    for p in patrones:
        rutas += sorted(glob.glob(p))
    if not rutas:
        raise SystemExit(f'No se encontro ningun CSV con {patrones}')

    print('=' * 78)
    print('  1. SESIONES Y RUIDO DE REINICIO')
    print('=' * 78)
    print(f"{'sesion':44}{'filas':>7}{'spawn':>8}{'quieto':>8}{'disp':>6}{'pase':>6}")

    validas, n_desc, filas_desc = [], 0, 0
    for r in rutas:
        d = pd.read_csv(r)
        faltan = [c for c in FEATURES + ACCIONES + MASCARA_SPAWN if c not in d.columns]
        if faltan:
            n_desc += 1
            filas_desc += len(d)
            print(f'{os.path.basename(r):44}{len(d):7}   esquema antiguo (falta {faltan[0]})')
            continue
        sm = np.logical_and.reduce([d[c].values == 0 for c in MASCARA_SPAWN])
        quieto = (clase_mov(d) == 4).mean() * 100
        print(f'{os.path.basename(r):44}{len(d):7}{sm.mean()*100:7.1f}%{quieto:7.1f}%'
              f"{int(d['Disparo'].sum()):6}{int(d['Pase'].sum()):6}")
        validas.append((os.path.basename(r), d, sm))

    if not validas:
        raise SystemExit('Ninguna sesion tiene el esquema actual.')

    n = sum(len(d) for _, d, _ in validas)
    spawn = sum(int(s.sum()) for _, _, s in validas)
    print(f'\n  sesiones validas   : {len(validas)} ({n} filas, {n*DT/60:.1f} min)')
    print(f'  sesiones descartadas: {n_desc} ({filas_desc} filas de esquema antiguo)')
    print(f'  ruido de reinicio  : {spawn} filas ({spawn/n*100:.1f}% del total)')

    print('\n' + '=' * 78)
    print('  2. SINCRONIA DE LAS ETIQUETAS DE ACCION')
    print('=' * 78)

    bruto = {c: 0 for c in ACCIONES}
    sin_balon = recolocadas = descartadas = 0
    procesadas = []
    for nombre, d, _ in validas:
        d = d.reset_index(drop=True).copy()
        for c in ACCIONES:
            bruto[c] += int(d[c].sum())
            for idx in d.index[d[c] == 1].tolist():
                if d.loc[idx, 'TienePelota'] == 1:
                    continue
                sin_balon += 1
                movida = False
                for off in range(1, LOOKBACK + 1):
                    j = idx - off
                    if j < 0:
                        break
                    if d.loc[j, 'TienePelota'] == 1:
                        d.loc[idx, c] = 0
                        d.loc[j, c] = 1
                        movida = True
                        recolocadas += 1
                        break
                if not movida:
                    d.loc[idx, c] = 0
                    descartadas += 1
        for c in ACCIONES:                              # ventana de intencion
            m = d[c].values.copy()
            for i in d.index[d[c] == 1].tolist():
                for off in range(1, VENTANA_INTENCION):
                    j = i - off
                    if j < 0:
                        break
                    m[j] = 1
            d[c] = m
        procesadas.append(d)

    total_bruto = sum(bruto.values())
    raw = pd.concat(procesadas, ignore_index=True)
    print(f"  eventos en bruto            : {'  '.join(f'{c}={bruto[c]}' for c in ACCIONES)}"
          f'  (total {total_bruto})')
    print(f'  etiquetados sin balon       : {sin_balon} ({sin_balon/max(total_bruto,1)*100:.1f}%)')
    print(f'    recolocados hacia atras   : {recolocadas} (hasta {LOOKBACK} fotogramas)')
    print(f'    descartados               : {descartadas}')
    print(f"  tras la ventana de intencion: "
          f"{'  '.join(f'{c}={int(raw[c].sum())}' for c in ACCIONES)}"
          f'  (ventana de {VENTANA_INTENCION} fotogramas = {VENTANA_INTENCION*DT:.1f} s)')

    print('\n' + '=' * 78)
    print('  3. DESBALANCE REAL FRENTE A DESBALANCE ARTIFICIAL')
    print('=' * 78)
    g = (raw['TienePelota'] > 0.5).values
    print(f"{'accion':12}{'% sobre TODOS':>18}{'% en su puerta':>18}"
          f"{'ratio global':>16}{'ratio en puerta':>18}")
    for c in ACCIONES:
        todo, dentro = raw[c].mean(), raw[c].values[g].mean()
        print(f'{c:12}{todo*100:17.2f}%{dentro*100:17.1f}%'
              f"{'1:'+str(round((1-todo)/max(todo,1e-9))):>16}"
              f"{'1:'+str(round((1-dentro)/max(dentro,1e-9))):>18}")
    print(f'\n  fotogramas con balon: {g.sum()} de {len(raw)} ({g.mean()*100:.1f}%)')
    print('  -> La parte del desbalance que desaparece al restringir la evaluacion a')
    print('     los fotogramas con balon era artificial: venia de preguntar "deberias')
    print('     pasar?" en estados donde pasar es imposible.')

    print('\n' + '=' * 78)
    print('  4. MOVIMIENTO')
    print('=' * 78)
    mov = clase_mov(raw)
    vc = pd.Series(mov).value_counts(normalize=True).sort_index() * 100
    print('  reparto de las 9 clases (%): '
          + '  '.join(f'c{int(k)}={v:.1f}' for k, v in vc.items()))
    prev = np.concatenate([[mov[0]], mov[:-1]])
    print(f'  clase mayoritaria          : c{int(vc.idxmax())} con {vc.max():.1f}%')
    print(f'  azar ({N_MOV} clases)            : {100/N_MOV:.1f}%')
    print(f'  persistencia               : {(prev == mov).mean()*100:.1f}%')
    print('\n  (La persistencia se calcula aqui sobre el conjunto completo; la cifra')
    print('   que va en la comparativa es la del conjunto de test.)')


if __name__ == '__main__':
    main(sys.argv[1:] or ['datos/SoccerData_*.csv'])
