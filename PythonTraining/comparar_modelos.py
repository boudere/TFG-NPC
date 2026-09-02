"""
comparar_modelos.py - Cual de tus modelos juega mejor
=====================================================
Coge las partidas grabadas por AIRecorder, las agrupa por modelo y las compara
entre si y contra el jugador humano.

"Jugar mejor" son dos cosas distintas y aqui se miden por separado:

  FIDELIDAD    se parece a ti. Es el eje que de verdad evalua el objetivo del
               TFG, que es aprendizaje por imitacion. Se mide con el
               solapamiento de ocupacion del campo y con el ritmo de cada
               accion respecto al tuyo (1.00 = identico).

  COMPETENCIA  juega bien en terminos futbolisticos, independientemente de si
               se parece a ti: cuanta posesion tiene, cuanto se acerca al
               balon, cuantas acciones llega a ejecutar.

Un modelo puede ser fiel y malo (te copia, y tu tampoco eras Messi) o bueno y
poco fiel. Los dos numeros hacen falta.

AVISO IMPORTANTE SOBRE EL SOLAPAMIENTO
--------------------------------------
El solapamiento compara DONDE pisa el agente con DONDE pisabas tu. Si grabas al
modelo jugando de defensa y tu referencia humana es sobre todo de delantero, el
numero baja aunque el modelo imite perfectamente: estara midiendo la posicion,
no la imitacion. Por eso el script compara la zona media de cada modelo con la
tuya y avisa cuando no coinciden. Para una comparativa limpia:

    - juega con los dos modelos DESDE EL MISMO PUESTO,
    - y usa --humano para quedarte con las sesiones tuyas de ese mismo puesto.

Uso:
    cd PythonTraining
    python comparar_modelos.py
    python comparar_modelos.py --modelos 5minutos 10minutos
    python comparar_modelos.py --modelos 5minutos 10minutos --humano 2026_09
"""
import argparse
import os
import sys

import numpy as np
import pandas as pd

import heatmap as H

DT = 0.1
MIN_RECOMENDADO = 3.0     # minutos por debajo de los cuales el dato es flojo
ZONA_TOLERANCIA = 150.0   # unidades de mundo de diferencia en X que ya preocupan

AZUL, NARANJA = '#2a78d6', '#c9541f'
TINTA, TINTA_2, LINEA = '#10160f', '#4d574b', '#d7ded4'
FONDO = '#fbfcfa'


# ------------------------------------------------------------- utilidades
def pulsaciones(serie):
    """Flancos 0->1: cada uno es una accion ejecutada, no un frame."""
    l = np.asarray(serie, dtype=int)
    if len(l) == 0:
        return 0
    return int(((l[1:] == 1) & (l[:-1] == 0)).sum() + (1 if l[0] == 1 else 0))


def cambios_direccion(df):
    v = list(zip(np.sign(df['InputX']), np.sign(df['InputZ'])))
    if len(v) < 2:
        return 0.0
    n = sum(1 for i in range(1, len(v)) if v[i] != v[i - 1] and v[i] != (0, 0))
    return n / (len(v) * DT)


def nombre_de(fichero):
    """AIData_<modelo>_YYYY_MM_DD_HH_MM_SS.csv -> <modelo>"""
    partes = os.path.splitext(os.path.basename(fichero))[0].split('_')
    return '_'.join(partes[1:-6]) if len(partes) >= 8 else '(sin nombre)'


def perfil(df):
    """Las cifras que describen como juega un agente."""
    m = len(df) * DT / 60
    p = {'minutos': m, 'frames': len(df)}
    for c in ['Disparo', 'Pase', 'RoboK', 'RoboL']:
        p[c] = pulsaciones(df[c]) / m if c in df.columns and m > 0 else float('nan')
    p['posesion'] = df['TienePelota'].mean() * 100 if 'TienePelota' in df else float('nan')
    p['dist_balon'] = df['DistPelota'].mean() if 'DistPelota' in df else float('nan')
    p['cambios'] = cambios_direccion(df)
    quieto = (df['InputX'].abs() < 0.5) & (df['InputZ'].abs() < 0.5)
    p['quieto'] = quieto.mean() * 100
    return p


def techo_para(hum, n):
    """
    Tu contra ti mismo, recortando cada trozo a n frames. Sin recortar, una
    grabacion corta siempre saldria peor que una larga solo por el tamanyo de
    muestra, y estariamos midiendo eso en vez del comportamiento.
    """
    vals = []
    for f, sub in hum.groupby('_fichero'):
        if len(sub) < n:
            continue
        resto = hum[hum['_fichero'] != f]
        if len(resto) == 0:
            continue
        rx, rz, _, _, _ = H.posiciones_humano(resto)
        g_resto, _, _ = H.rejilla(rx, rz)
        for ini in range(0, len(sub) - n + 1, n):
            sx, sz, _, _, _ = H.posiciones_humano(sub.iloc[ini:ini + n])
            g_sub, _, _ = H.rejilla(sx, sz)
            vals.append(H.similitud(g_resto, g_sub) * 100)
    return (float(np.median(vals)) if vals else float('nan')), len(vals)


def suelo_para(g_hum, n, repeticiones=20):
    """Un jugador que se mueve al azar por el campo, con la misma muestra."""
    rng = np.random.default_rng(0)
    v = []
    for _ in range(repeticiones):
        g, _, _ = H.rejilla(rng.uniform(H.PORTERIA_1[0], H.PORTERIA_2[0], n),
                            rng.uniform(H.Z_MIN, H.Z_MAX, n))
        v.append(H.similitud(g_hum, g))
    return float(np.mean(v)) * 100


# ------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--modelos', nargs='*', default=None,
                    help='nombres a comparar; por defecto, todos los encontrados')
    ap.add_argument('--humano', default=None,
                    help='subcadena para quedarse solo con ciertas sesiones humanas')
    ap.add_argument('--figura', default='comparativa_modelos.png')
    a = ap.parse_args()

    print('=' * 78)
    print('  CUAL DE TUS MODELOS JUEGA MEJOR')
    print('=' * 78)

    # --- referencia humana ---
    print('\n[1/4] Referencia humana')
    hum = H.cargar('SoccerData_*.csv', 'Humano',
                   filtro_modelo=a.humano, requeridas=H.COLUMNAS_HUMANO)
    if hum is None:
        sys.exit('No hay grabaciones humanas con el esquema actual.')
    hx, hz, _, _, _ = H.posiciones_humano(hum)
    g_hum, _, _ = H.rejilla(hx, hz)
    zona_hum = float(np.median(hx))
    ph = perfil(hum)
    print(f"  {ph['minutos']:.1f} min | disparo {ph['Disparo']:.2f}/min  pase {ph['Pase']:.2f}/min  "
          f"roboK {ph['RoboK']:.2f}/min  roboL {ph['RoboL']:.2f}/min")
    print(f"  posesion {ph['posesion']:.1f}%  dist. balon {ph['dist_balon']:.0f}  "
          f"cambios {ph['cambios']:.2f}/s  zona media X {zona_hum:.0f}")

    # --- partidas de cada modelo ---
    print('\n[2/4] Partidas de los modelos')
    porm = {}
    for f in H._buscar('AIData_*.csv'):
        n = nombre_de(f)
        if a.modelos and n not in a.modelos:
            continue
        try:
            d = pd.read_csv(f)
        except Exception as e:
            print(f'    {os.path.basename(f)}: ilegible ({e})')
            continue
        if 'AbsMyPosX' not in d.columns:
            print(f'    {os.path.basename(f)}: formato antiguo, se descarta')
            continue
        porm.setdefault(n, []).append(d)
        print(f'    {os.path.basename(f)}: {len(d)} frames -> "{n}"')

    if not porm:
        sys.exit('No hay partidas utilizables. Juega con cada modelo y vuelve.')

    # --- perfil de cada modelo ---
    print('\n[3/4] Escala de referencia y perfiles')
    filas = []
    for n, partidas in sorted(porm.items()):
        d = pd.concat(partidas, ignore_index=True)
        p = perfil(d)
        p['modelo'] = n
        p['partidas'] = len(partidas)

        g_m, _, _ = H.rejilla(d['AbsMyPosX'].values, d['AbsMyPosZ'].values)
        p['solapamiento'] = H.similitud(g_hum, g_m) * 100

        # Suelo y techo con SU numero de frames: comparar una grabacion de 1 min
        # contra un techo calculado con 5 min seria hacer trampa a su favor.
        p['suelo'], p['techo'] = suelo_para(g_hum, len(d)), techo_para(hum, len(d))[0]
        rango = p['techo'] - p['suelo']
        p['fidelidad_zona'] = (float(np.clip((p['solapamiento'] - p['suelo']) / rango, 0, 1)) * 100
                               if np.isfinite(rango) and rango > 1 else float('nan'))

        p['zona'] = float(np.median(d['AbsMyPosX'].values))
        p['desvio_zona'] = p['zona'] - zona_hum

        # ritmo de acciones frente al humano: 1.00 = mismo comportamiento
        for c in ['Disparo', 'Pase', 'RoboK', 'RoboL', 'cambios', 'posesion']:
            p['r_' + c] = p[c] / ph[c] if ph[c] else float('nan')
        r = np.array([p['r_' + c] for c in ['Disparo', 'Pase', 'RoboK', 'RoboL', 'cambios']], float)
        p['fidelidad_ritmo'] = float(np.nanmean(np.exp(-np.abs(np.log(np.clip(r, 1e-3, None))))) * 100)
        filas.append(p)

    df = pd.DataFrame(filas)
    df.to_csv('comparativa_modelos.csv', index=False)

    # --- tablas ---
    print('\n[4/4] Resultados')
    print(f"\n{'modelo':16} {'part':>4} {'min':>5} {'solap':>7} {'suelo':>6} {'techo':>6} "
          f"{'FID.ZONA':>9} {'FID.RITMO':>10}")
    print('-' * 78)
    for _, r in df.iterrows():
        aviso = '  (!)' if r['minutos'] < MIN_RECOMENDADO else ''
        fz = f"{r['fidelidad_zona']:8.0f}%" if np.isfinite(r['fidelidad_zona']) else '       --'
        print(f"{r['modelo'][:16]:16} {int(r['partidas']):4} {r['minutos']:5.1f} "
              f"{r['solapamiento']:6.1f}% {r['suelo']:5.1f}% {r['techo']:5.1f}% "
              f"{fz} {r['fidelidad_ritmo']:9.0f}%{aviso}")

    print(f"\n{'modelo':16} {'poses':>7} {'dist.balon':>11} {'camb/s':>8} {'quieto':>8} {'zonaX':>7}")
    print('-' * 78)
    for _, r in df.iterrows():
        print(f"{r['modelo'][:16]:16} {r['posesion']:6.1f}% {r['dist_balon']:11.0f} "
              f"{r['cambios']:8.2f} {r['quieto']:7.0f}% {r['zona']:7.0f}")
    print(f"{'HUMANO (tu)':16} {ph['posesion']:6.1f}% {ph['dist_balon']:11.0f} "
          f"{ph['cambios']:8.2f} {ph['quieto']:7.0f}% {zona_hum:7.0f}")

    print(f"\n{'modelo':16} " + ' '.join(f'{c:>11}' for c in ['Disparo', 'Pase', 'RoboK', 'RoboL']))
    print('  (acciones por minuto, entre parentesis el ratio frente a ti)')
    print('-' * 78)
    for _, r in df.iterrows():
        cs = ' '.join(f"{r[c]:5.2f}({r['r_' + c]:.1f})" for c in ['Disparo', 'Pase', 'RoboK', 'RoboL'])
        print(f"{r['modelo'][:16]:16} {cs}")
    print(f"{'HUMANO (tu)':16} " + ' '.join(f'{ph[c]:5.2f}(1.0)' for c in
                                            ['Disparo', 'Pase', 'RoboK', 'RoboL']))

    # --- avisos ---
    avisos = []
    flojas = df[df['minutos'] < MIN_RECOMENDADO]
    if len(flojas):
        avisos.append(f"{', '.join(flojas['modelo'])}: menos de {MIN_RECOMENDADO:.0f} min "
                      f"grabados. Con tan poco, estas cifras son orientativas.")
    fuera = df[df['desvio_zona'].abs() > ZONA_TOLERANCIA]
    for _, r in fuera.iterrows():
        avisos.append(f"{r['modelo']}: juega en X~{r['zona']:.0f} y tu referencia esta en "
                      f"X~{zona_hum:.0f}. Son PUESTOS distintos, asi que su solapamiento "
                      f"mide la posicion, no la imitacion.")
    zonas = df['zona'].values
    if len(zonas) >= 2 and (zonas.max() - zonas.min()) > ZONA_TOLERANCIA:
        avisos.append("los modelos no se grabaron desde el mismo puesto, asi que sus "
                      "solapamientos NO son comparables entre si. Repite las partidas "
                      "colocandote siempre en el mismo jugador.")
    if avisos:
        print('\n' + '-' * 78)
        for t in avisos:
            print('  (!) ' + t)

    # --- veredicto ---
    if len(df) >= 2:
        print('\n' + '-' * 78)
        comparables = (zonas.max() - zonas.min()) <= ZONA_TOLERANCIA
        mf = df.loc[df['fidelidad_zona'].idxmax()] if df['fidelidad_zona'].notna().any() else None
        mr = df.loc[df['fidelidad_ritmo'].idxmax()]
        mc = df.loc[df['posesion'].idxmax()]
        if mf is not None:
            print(f"  Ocupa el campo como tu : {mf['modelo']} ({mf['solapamiento']:.1f}% de "
                  f"solapamiento; suelo {mf['suelo']:.0f}%, techo {mf['techo']:.0f}%)")
        print(f"  Actua a tu ritmo       : {mr['modelo']} ({mr['fidelidad_ritmo']:.0f}%)")
        print(f"  Mas competente         : {mc['modelo']} ({mc['posesion']:.1f}% de posesion, "
              f"distancia media al balon {mc['dist_balon']:.0f})")
        if not comparables:
            print("  ...pero ver el aviso de arriba: con puestos distintos el veredicto")
            print("     de fidelidad espacial no vale.")

    dibujar(df, ph, a.figura)
    print(f'\nGuardado: comparativa_modelos.csv y {a.figura}')


# ------------------------------------------------------------------ figura
def dibujar(df, ph, ruta):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt

    fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(13, 4.8), facecolor=FONDO)
    y = np.arange(len(df))
    alto = 0.55

    # --- panel 1: fidelidad espacial ---
    # El tramo por debajo del suelo lo consigue cualquiera moviendose al azar,
    # asi que se pinta en gris: lo unico que cuenta es lo que sobresale.
    ax1.set_facecolor(FONDO)
    tope = float(np.nanmax([df['solapamiento'].max(), df['techo'].max(), 10]))
    for i, (_, r) in enumerate(df.iterrows()):
        gris = min(r['solapamiento'], r['suelo'])
        ax1.barh(i, gris, alto, color='#e4e8e1', zorder=3)
        if r['solapamiento'] > r['suelo']:
            ax1.barh(i, r['solapamiento'] - r['suelo'], alto, left=r['suelo'],
                     color=AZUL, zorder=3)
        ax1.plot([r['suelo']] * 2, [i - alto / 2 - .06, i + alto / 2 + .06],
                 color=TINTA_2, lw=1.4, zorder=5)
        if np.isfinite(r['techo']):
            ax1.plot([r['techo']] * 2, [i - alto / 2 - .06, i + alto / 2 + .06],
                     color=TINTA_2, lw=1.4, ls=(0, (2, 2)), zorder=5)
        # La etiqueta va al final de la barra, salvo que ahi este la linea del
        # techo; en ese caso se corre para no montarse encima.
        fin = max(r['solapamiento'], r['suelo'])
        if np.isfinite(r['techo']) and abs(r['techo'] - fin) < tope * 0.10:
            fin = r['techo']
        ax1.text(fin + tope * 0.015, i, f"{r['solapamiento']:.0f}%",
                 va='center', fontsize=9, color=TINTA)
    # Etiquetas sobre la fila de arriba, en lugar de una leyenda que tape barras.
    arriba = len(df) - 1
    ax1.text(df['suelo'].iloc[arriba], arriba + alto / 2 + 0.14, 'azar',
             color=TINTA_2, fontsize=8.5, ha='center', va='bottom')
    if np.isfinite(df['techo'].iloc[arriba]):
        ax1.text(df['techo'].iloc[arriba], arriba + alto / 2 + 0.14, 'tu techo',
                 color=TINTA_2, fontsize=8.5, ha='center', va='bottom')
    ax1.set_yticks(y); ax1.set_yticklabels(df['modelo'])
    ax1.set_ylim(-0.6, len(df) - 0.10)
    ax1.set_xlabel('Solapamiento con tus posiciones (%)', color=TINTA_2, fontsize=9.5)
    ax1.set_title('Ocupa el campo como tu', color=TINTA, fontsize=12,
                  fontweight='bold', loc='left', pad=30)
    ax1.text(0, 1.03, 'En gris, lo que ya da el azar. Solo cuenta el tramo azul.',
             transform=ax1.transAxes, color=TINTA_2, fontsize=8.5, va='bottom')
    ax1.set_xlim(0, tope * 1.22)

    # --- panel 2: ritmo de acciones frente al humano ---
    ax2.set_facecolor(FONDO)
    accs = ['Disparo', 'Pase', 'RoboK', 'RoboL']
    ancho = 0.8 / max(len(df), 1)
    cols = [AZUL, NARANJA, '#1baf7a', '#eda100', '#8452c9']
    for i, (_, r) in enumerate(df.iterrows()):
        xs = np.arange(len(accs)) + i * ancho - 0.4 + ancho / 2
        ax2.bar(xs, [r['r_' + c] for c in accs], ancho * 0.9,
                color=cols[i % len(cols)], label=r['modelo'], zorder=3)
    ax2.axhline(1.0, color=TINTA_2, lw=1.5, ls=(0, (5, 4)), zorder=2)
    ax2.text(-0.48, 1.02, 'tu ritmo', color=TINTA_2, fontsize=8.5, ha='left', va='bottom')
    ax2.set_xticks(np.arange(len(accs))); ax2.set_xticklabels(accs)
    ax2.set_ylabel('Veces tu ritmo (1 = igual)', color=TINTA_2, fontsize=9.5)
    ax2.set_ylim(0, max(1.25, float(np.nanmax(
        [r['r_' + c] for _, r in df.iterrows() for c in accs])) * 1.12))
    ax2.set_title('Actua con tu frecuencia', color=TINTA, fontsize=12,
                  fontweight='bold', loc='left', pad=30)
    leg2 = ax2.legend(frameon=False, fontsize=8.5, ncol=min(len(df), 4),
                      loc='lower left', bbox_to_anchor=(0, 1.0), columnspacing=1.2,
                      handlelength=1.1, handletextpad=0.5)
    for t in leg2.get_texts():
        t.set_color(TINTA)

    for ax in (ax1, ax2):
        ax.grid(axis='x' if ax is ax1 else 'y', color=LINEA, lw=0.8, zorder=0)
        ax.set_axisbelow(True)
        for s in ('top', 'right'):
            ax.spines[s].set_visible(False)
        for s in ('left', 'bottom'):
            ax.spines[s].set_color(LINEA)
        ax.tick_params(colors=TINTA_2, labelsize=9)

    fig.tight_layout()
    fig.savefig(ruta, dpi=150, facecolor=FONDO)


if __name__ == '__main__':
    main()
