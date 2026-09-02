"""
duracion.py - Cuanto aporta grabar mas minutos
==============================================
Parte UNA sesion en trozos crecientes y entrena un modelo con cada trozo,
midiendolos todos contra el MISMO conjunto de test.

Por que una sola sesion y no dos partidas distintas: si grabas 5 minutos un
dia y 10 otro, al comparar mezclas dos efectos, la cantidad de datos y que son
partidas distintas (otro rival, otra suerte, otro dia tuyo). Partiendo una
unica grabacion lo unico que cambia es cuantos minutos ve el modelo, que es
justo lo que se quiere medir.

Cada configuracion se entrena con varias semillas, porque ya comprobamos que
una sola ejecucion mueve el F1 de las acciones hasta diecisiete puntos.

Uso:
    # recomendado: otras sesiones como test, la de 10 min entera para entrenar
    python duracion.py --sesion ../Assets/SoccerData_10min.csv \
                       --test ../Assets/SoccerData_2026_08_2*.csv

    # sin test aparte: reserva el ultimo 20% de la propia sesion
    python duracion.py --sesion ../Assets/SoccerData_10min.csv

    # curva completa en vez de solo mitad y entero
    python duracion.py --sesion ... --fracciones 0.25 0.5 0.75 1.0
"""
import argparse
import os

import numpy as np
import pandas as pd
import torch

import comun
from comun import FEATURES, ACCIONES
from arquitecturas import FNNDualHead
import comparar as C

# Paleta: azul para movimiento, naranja para acciones, como en los mapas de calor.
AZUL, NARANJA = '#2a78d6', '#c9541f'
TINTA, TINTA_2, LINEA = '#10160f', '#4d574b', '#d7ded4'
FONDO = '#fbfcfa'


def arrays(df, mean, std):
    X = (df[FEATURES].values.astype(np.float32) - mean) / std
    return (X, df['_mov'].values.astype(np.int64),
            df[ACCIONES].values.astype(np.float32), df['_puerta'].values.astype(bool))


def escalador_de(df):
    X = df[FEATURES].values.astype(np.float32)
    m, s = X.mean(0), X.std(0)
    s[s < 1e-6] = 1.0
    return m, s


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--sesion', required=True, help='CSV a trocear (la sesion larga)')
    ap.add_argument('--test', nargs='*', default=None,
                    help='CSV de test. Si se omite, se reserva el ultimo 20% de la sesion')
    ap.add_argument('--fracciones', nargs='*', type=float, default=[0.5, 1.0])
    ap.add_argument('--semillas', type=int, default=3)
    ap.add_argument('--epocas', type=int, default=150)
    ap.add_argument('--figura', default='curva_duracion.png')
    a = ap.parse_args()

    print('=' * 74)
    print('  CUANTO APORTA GRABAR MAS MINUTOS')
    print('=' * 74)

    print('\n[1/3] Cargando')
    ses = comun.cargar_sesiones([a.sesion])
    if not ses:
        raise SystemExit('La sesion no tiene el esquema actual.')
    entera = ses[0]

    if a.test:
        test_ses = comun.cargar_sesiones(a.test)
        test_ses = [s for s in test_ses
                    if s['_sesion'].iloc[0] != entera['_sesion'].iloc[0]]
        if not test_ses:
            raise SystemExit('El test no puede ser la misma sesion que se trocea.')
        pool = entera
        modo = f'{len(test_ses)} sesion(es) aparte'
    else:
        corte = int(len(entera) * 0.8)
        pool = entera.iloc[:corte].reset_index(drop=True)
        test_ses = [entera.iloc[corte:].reset_index(drop=True)]
        modo = 'ultimo 20% de la propia sesion'

    dte = pd.concat(test_ses, ignore_index=True)
    print(f'\n  sesion a trocear : {len(pool)} filas ({len(pool)*comun.DT/60:.1f} min)')
    print(f'  conjunto de test : {len(dte)} filas ({len(dte)*comun.DT/60:.1f} min) — {modo}')

    print('\n[2/3] Lineas base sobre ese test')
    base = comun.lineas_base(test_ses)
    for k, v in base.items():
        print(f'  {k:34} {v:5.1f}%')

    print(f'\n[3/3] Entrenando ({a.semillas} semillas x {len(a.fracciones)} tamanyos, '
          f'{a.epocas} epocas)')
    filas = []
    for fr in sorted(a.fracciones):
        n = max(1, int(len(pool) * fr))
        dtr = pool.iloc[:n].reset_index(drop=True)          # SIEMPRE desde el principio
        mins = n * comun.DT / 60
        # El escalador se recalcula con cada subconjunto: es lo que hace el
        # pipeline real cuando entrenas un modelo desde el juego.
        mean, std = escalador_de(dtr)
        Xtr, ymtr, yatr, _ = arrays(dtr, mean, std)
        Xte, ymte, yate, gte = arrays(dte, mean, std)

        rs = []
        for s in range(a.semillas):
            comun.fijar_semilla(s)
            m = FNNDualHead(len(FEATURES))
            C.entrenar_dual(m, Xtr, ymtr, yatr, a.epocas, C._pos_weight(yatr))
            lm, la = C.predecir(m, Xte)
            rs.append(comun.evaluar(ymte, lm.argmax(1), yate, C._sig(la), gte))

        fila = {'fraccion': fr, 'minutos': mins, 'frames': n}
        for k in ['acc_mov', 'f1_mov', 'f1_disparo', 'f1_pase']:
            v = np.array([r[k] for r in rs], float)
            fila[k] = np.nanmean(v)
            fila[k + '_sd'] = np.nanstd(v, ddof=1) if len(v) > 1 else 0.0
        filas.append(fila)
        print(f"  {mins:5.1f} min ({n:5} frames)  acc {fila['acc_mov']:5.1f}+/-{fila['acc_mov_sd']:4.1f}   "
              f"f1mov {fila['f1_mov']:5.1f}+/-{fila['f1_mov_sd']:4.1f}   "
              f"disp {fila['f1_disparo']:5.1f}   pase {fila['f1_pase']:5.1f}")

    df = pd.DataFrame(filas)
    df.to_csv('curva_duracion.csv', index=False)

    print('\n' + '=' * 74)
    print('RESULTADO')
    print('=' * 74)
    p = df.iloc[0]
    u = df.iloc[-1]
    d_acc = u['acc_mov'] - p['acc_mov']
    ruido = max(p['acc_mov_sd'], u['acc_mov_sd'])
    print(f"  {p['minutos']:.1f} min -> {u['minutos']:.1f} min: "
          f"la exactitud de movimiento cambia {d_acc:+.1f} puntos")
    print(f"  Ruido entre semillas en esa metrica: +/-{ruido:.1f}")
    if abs(d_acc) <= 2 * ruido:
        print("  -> La diferencia NO supera el ruido. Con estos datos, duplicar los")
        print("     minutos de grabacion no mejora el modelo de forma medible.")
    else:
        print("  -> La diferencia SI supera el ruido: mas minutos ayudan.")
    print(f"\n  Referencia: repetir el fotograma anterior acierta "
          f"{base['Persistencia (repetir anterior)']:.1f}%")

    dibujar(df, base, a.figura)
    print(f"\nGuardado: curva_duracion.csv y {a.figura}")


def dibujar(df, base, ruta):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt

    fig, ax = plt.subplots(figsize=(8, 5), facecolor=FONDO)
    ax.set_facecolor(FONDO)

    ax.axhline(base['Persistencia (repetir anterior)'], color=TINTA_2, lw=1,
               ls=(0, (5, 4)), zorder=1)
    ax.text(df['minutos'].max(), base['Persistencia (repetir anterior)'] + 1.5,
            f"persistencia  {base['Persistencia (repetir anterior)']:.0f}%",
            ha='right', va='bottom', fontsize=9, color=TINTA_2)
    ax.axhline(base['Clase mayoritaria'], color=LINEA, lw=1, zorder=1)
    ax.text(df['minutos'].max(), base['Clase mayoritaria'] + 1.0,
            f"clase mayoritaria  {base['Clase mayoritaria']:.0f}%",
            ha='right', va='bottom', fontsize=9, color=TINTA_2)

    for col, sd, color, etq in [('acc_mov', 'acc_mov_sd', AZUL, 'Exactitud de movimiento'),
                                ('f1_mov', 'f1_mov_sd', NARANJA, 'F1 de movimiento')]:
        ax.errorbar(df['minutos'], df[col], yerr=df[sd], color=color, lw=2,
                    marker='o', ms=7, capsize=4, mec=FONDO, mew=1.5, label=etq, zorder=3)

    ax.set_xlabel('Minutos de grabacion usados para entrenar', color=TINTA_2, fontsize=10)
    ax.set_ylabel('% sobre el conjunto de test', color=TINTA_2, fontsize=10)
    ax.set_title('Cuanto aporta grabar mas minutos', color=TINTA, fontsize=13,
                 fontweight='bold', loc='left', pad=30)
    ax.text(0, 1.03, 'Barras: desviacion entre semillas. Mismo test en todos los puntos.',
            transform=ax.transAxes, color=TINTA_2, fontsize=9, va='bottom')
    ax.set_xticks(df['minutos'].tolist())
    ax.set_xticklabels([f'{m:.1f}' for m in df['minutos']])
    ax.grid(axis='y', color=LINEA, lw=0.8, zorder=0)
    ax.set_axisbelow(True)
    for s in ('top', 'right'):
        ax.spines[s].set_visible(False)
    for s in ('left', 'bottom'):
        ax.spines[s].set_color(LINEA)
    ax.tick_params(colors=TINTA_2, labelsize=9)
    ax.set_ylim(0, max(base['Persistencia (repetir anterior)'] + 12, df['acc_mov'].max() + 12))
    leg = ax.legend(frameon=False, fontsize=9.5, loc='upper left')
    for t in leg.get_texts():
        t.set_color(TINTA)
    fig.tight_layout()
    fig.savefig(ruta, dpi=150, facecolor=FONDO)


if __name__ == '__main__':
    main()
