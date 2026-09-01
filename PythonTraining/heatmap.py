"""
heatmap.py - Mapas de calor: humano de entrenamiento vs modelo en juego

Compara DONDE estuvo el jugador humano mientras grababa el dataset con DONDE
esta el NPC que aprendio de el. Es la comprobacion visual de si el modelo
reproduce la ocupacion del campo del jugador o se queda en otra zona.

Uso:
    cd TFG-NPC
    python PythonTraining/heatmap.py
    python PythonTraining/heatmap.py --modelo ModeloChidisimo

Entradas (se buscan en Assets/ y en persistentDataPath):
    SoccerData_*.csv   grabaciones del humano (Recorder.cs)
    AIData_*.csv       grabaciones del NPC     (AIRecorder.cs)

Salida: Assets/heatmaps/*.png

Nota sobre coordenadas
----------------------
Recorder.cs no guarda la posicion absoluta, solo la posicion de las dos
porterias RELATIVA al jugador. Se recupera la absoluta invirtiendo esa resta:
    myPos = posPorteriaRival - RelPorteriaRival
Las porterias son fijas, asi que la reconstruccion es exacta (se comprueba
contra la porteria propia y se avisa si las dos cuentas no coinciden).
"""

import argparse
import glob
import os
import sys
from pathlib import Path

import numpy as np
import pandas as pd
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import matplotlib.patches as patches
from matplotlib.colors import LinearSegmentedColormap
from scipy.ndimage import gaussian_filter

# ---------------------------------------------------------------- rutas
_HERE = Path(__file__).parent
_ASSETS = (_HERE / '../Assets').resolve()
OUTPUT_DIR = _ASSETS / 'heatmaps'

# ------------------------------------------------- geometria del campo
# Posiciones de mundo de las porterias, leidas de Assets/Scenes/Field.unity
# (objetos Porteria1 y Porteria2, ambos sin padre, asi que su m_LocalPosition
# es directamente la posicion global).
PORTERIA_1 = np.array([-528.0, 30.9])
PORTERIA_2 = np.array([679.3, 137.9])

X_MIN, X_MAX = -620.0, 780.0
Z_MIN, Z_MAX = -320.0, 500.0
GRID_W, GRID_H = 110, 64
SIGMA = 2.0

# --------------------------------------------------------------- color
# Sequencial de un solo tono, claro -> oscuro. El mapa arcoiris que habia
# antes (negro -> morado -> rojo -> amarillo) inventa fronteras donde la
# densidad cambia suave y ordena mal los valores intermedios.
SURFACE = '#fcfcfb'
INK = '#0b0b0b'
INK_2 = '#52514e'
GRID_INK = '#c9c8c3'

RAMPA_HUMANO = ['#cde2fb', '#9ec5f4', '#5598e7', '#2a78d6', '#1c5cab', '#0d366b']
RAMPA_MODELO = ['#fde3d6', '#f9bd9e', '#f18f61', '#eb6834', '#b7461d', '#6b2610']
AZUL = '#2a78d6'
NARANJA = '#eb6834'
NEUTRO = '#f0efec'


def _cmap(rampa, nombre):
    """Rampa secuencial: transparente cerca de cero para que se vea el campo."""
    from matplotlib.colors import to_rgb
    cols = [(*to_rgb(rampa[0]), 0.0)] + [(*to_rgb(c), 1.0) for c in rampa]
    return LinearSegmentedColormap.from_list(nombre, cols)


CMAP_HUMANO = _cmap(RAMPA_HUMANO, 'humano')
CMAP_MODELO = _cmap(RAMPA_MODELO, 'modelo')
# Divergente: dos tonos opuestos con gris neutro en el centro. Azul = zona del
# humano, naranja = zona del modelo, gris = los dos igual.
def _cmap_dif():
    from matplotlib.colors import to_rgb
    # El centro va transparente para que las lineas del campo se sigan viendo:
    # un gris opaco tapaba el campo entero, que es justo la mitad de la imagen.
    tramos = [(*to_rgb(RAMPA_HUMANO[5]), 1.0), (*to_rgb(RAMPA_HUMANO[3]), 0.95),
              (*to_rgb(RAMPA_HUMANO[1]), 0.55), (*to_rgb(NEUTRO), 0.0),
              (*to_rgb(RAMPA_MODELO[1]), 0.55), (*to_rgb(RAMPA_MODELO[3]), 0.95),
              (*to_rgb(RAMPA_MODELO[5]), 1.0)]
    return LinearSegmentedColormap.from_list('dif', tramos)


CMAP_DIF = _cmap_dif()


# ================================================================= carga
def _buscar(patron):
    rutas = sorted(glob.glob(str(_ASSETS / patron)))
    for extra in _persistent_dirs():
        rutas += sorted(glob.glob(os.path.join(extra, patron)))
    # sin duplicados, conservando el orden
    vistos, out = set(), []
    for r in rutas:
        k = os.path.basename(r)
        if k not in vistos:
            vistos.add(k)
            out.append(r)
    return out


def _persistent_dirs():
    """persistentDataPath de Unity en Windows y en Linux."""
    cands = []
    appdata = os.environ.get('USERPROFILE')
    if appdata:
        cands.append(os.path.join(appdata, 'AppData', 'LocalLow'))
    home = os.path.expanduser('~')
    cands.append(os.path.join(home, '.config', 'unity3d'))
    out = []
    for base in cands:
        if os.path.isdir(base):
            for root, dirs, _ in os.walk(base):
                if root.count(os.sep) - base.count(os.sep) > 2:
                    dirs[:] = []
                    continue
                out.append(root)
    return out


def posiciones_humano(df):
    """
    Devuelve (x, z) del jugador y (bx, bz) de la pelota en coordenadas de mundo,
    reconstruidas a partir de las porterias.
    """
    delta = float((df['RelPorteriaRivalX'] - df['RelPorteriaPropiaX']).median())
    d12 = PORTERIA_2[0] - PORTERIA_1[0]
    if abs(delta - d12) <= abs(delta + d12):
        rival, propia = PORTERIA_2, PORTERIA_1
    else:
        rival, propia = PORTERIA_1, PORTERIA_2

    x = rival[0] - df['RelPorteriaRivalX'].values
    z = rival[1] - df['RelPorteriaRivalZ'].values

    # Comprobacion: la porteria propia tiene que dar la misma posicion.
    x2 = propia[0] - df['RelPorteriaPropiaX'].values
    disc = float(np.max(np.abs(x - x2))) if len(x) else 0.0
    if disc > 1.0:
        print(f"    AVISO: las dos porterias discrepan hasta {disc:.1f} unidades. "
              f"Puede que este CSV se grabara con otro campo.")

    bx = x + df['RelPelotaX'].values
    bz = z + df['RelPelotaZ'].values
    return x, z, bx, bz, ('Porteria2' if rival is PORTERIA_2 else 'Porteria1')


def posiciones_modelo(df):
    """AIRecorder guarda ya la posicion absoluta."""
    return (df['AbsMyPosX'].values, df['AbsMyPosZ'].values,
            df['AbsBallPosX'].values, df['AbsBallPosZ'].values)


# Columnas sin las cuales un CSV humano no es comparable. Las grabaciones de
# julio salen de versiones anteriores del Recorder (88 y 69 columnas, otro
# conjunto de features) y ademas son anteriores a cambios de juego como la
# velocidad con balon o la distancia de robo. Mezclarlas con las actuales
# contamina la referencia: no son partidas de este juego ni entrenaron a este
# modelo. pd.concat las unia en silencio rellenando con NaN.
COLUMNAS_HUMANO = ['RelPorteriaRivalX', 'RelPorteriaRivalZ',
                   'RelPorteriaPropiaX', 'RelPorteriaPropiaZ',
                   'RelPelotaX', 'RelPelotaZ']


def cargar(patron, etiqueta, filtro_modelo=None, requeridas=None):
    ficheros = _buscar(patron)
    if filtro_modelo:
        ficheros = [f for f in ficheros if filtro_modelo.lower() in os.path.basename(f).lower()]
    if not ficheros:
        return None
    trozos = []
    for f in ficheros:
        try:
            d = pd.read_csv(f)
        except Exception as e:
            print(f"    {os.path.basename(f)}: ilegible ({e})")
            continue
        if requeridas:
            faltan = [c for c in requeridas if c not in d.columns]
            if faltan:
                print(f"    {os.path.basename(f)}: DESCARTADO, esquema antiguo "
                      f"({len(d.columns)} columnas, falta {faltan[0]})")
                continue
        d['_fichero'] = os.path.basename(f)
        trozos.append(d)
        print(f"    {os.path.basename(f)}: {len(d)} filas")
    if not trozos:
        return None
    raw = pd.concat(trozos, ignore_index=True)
    print(f"    [{etiqueta}] {len(raw)} filas en {len(trozos)} fichero(s) "
          f"= {len(raw) * 0.1 / 60:.1f} min\n")
    return raw


# ============================================================== rejillas
def rejilla(xs, zs):
    m = ((xs >= X_MIN) & (xs <= X_MAX) & (zs >= Z_MIN) & (zs <= Z_MAX) &
         np.isfinite(xs) & np.isfinite(zs))
    xs, zs = xs[m], zs[m]
    if len(xs) == 0:
        return None, 0, 0
    h, _, _ = np.histogram2d(xs, zs, bins=[GRID_W, GRID_H],
                             range=[[X_MIN, X_MAX], [Z_MIN, Z_MAX]])
    h = gaussian_filter(h, sigma=SIGMA)
    s = h.sum()
    if s > 0:
        h = h / s                      # densidad de probabilidad: comparable
    return h, len(xs), int(len(m) - m.sum())


def similitud(a, b):
    """Interseccion de histogramas: 1.0 = ocupan el campo igual, 0 = disjuntos."""
    if a is None or b is None:
        return float('nan')
    return float(np.minimum(a, b).sum())


# ================================================================ dibujo
def _campo(ax):
    ax.set_facecolor(SURFACE)
    ax.add_patch(patches.Rectangle(
        (PORTERIA_1[0], Z_MIN), PORTERIA_2[0] - PORTERIA_1[0], Z_MAX - Z_MIN,
        linewidth=1.2, edgecolor=GRID_INK, facecolor='none', zorder=6))
    medio = (PORTERIA_1[0] + PORTERIA_2[0]) / 2
    ax.axvline(medio, color=GRID_INK, linewidth=1, linestyle=(0, (4, 4)), zorder=6)
    for gx, gz, nombre in [(PORTERIA_1[0], PORTERIA_1[1], 'porteria'),
                           (PORTERIA_2[0], PORTERIA_2[1], 'porteria')]:
        ax.add_patch(patches.Rectangle((gx - 18, gz - 70), 36, 140,
                                       linewidth=1.4, edgecolor=INK_2,
                                       facecolor='none', zorder=6))
    ax.set_xlim(X_MIN, X_MAX)
    ax.set_ylim(Z_MIN, Z_MAX)
    ax.set_xticks([])
    ax.set_yticks([])
    for s in ax.spines.values():
        s.set_edgecolor(GRID_INK)
        s.set_linewidth(1)


def _pintar(ax, grid, cmap, vmax=None):
    if grid is None:
        ax.text(0.5, 0.5, 'sin datos', transform=ax.transAxes, ha='center',
                va='center', color=INK_2, fontsize=12)
        return None
    return ax.imshow(grid.T, extent=[X_MIN, X_MAX, Z_MIN, Z_MAX], origin='lower',
                     cmap=cmap, aspect='auto', interpolation='bilinear',
                     vmin=0, vmax=vmax if vmax else grid.max(), zorder=2)


def guardar(fig, nombre):
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    ruta = OUTPUT_DIR / nombre
    fig.savefig(ruta, dpi=150, bbox_inches='tight', facecolor=SURFACE)
    plt.close(fig)
    print(f"    {nombre}")
    return ruta


def mapa_simple(xs, zs, titulo, subtitulo, cmap, nombre):
    g, n, fuera = rejilla(xs, zs)
    fig, ax = plt.subplots(figsize=(11, 6.4), facecolor=SURFACE)
    _campo(ax)
    im = _pintar(ax, g, cmap)
    if im is not None:
        cb = fig.colorbar(im, ax=ax, shrink=0.75, pad=0.015)
        cb.set_label('densidad de presencia', color=INK_2, fontsize=9)
        cb.ax.tick_params(colors=INK_2, labelsize=8)
        cb.outline.set_edgecolor(GRID_INK)
    ax.set_title(titulo, color=INK, fontsize=13, fontweight='bold', loc='left', pad=22)
    ax.text(0, 1.02, subtitulo, transform=ax.transAxes,
            color=INK_2, fontsize=9, va='bottom')
    ax.text(0.012, 0.965, f'n = {n:,} frames', transform=ax.transAxes,
            color=INK_2, fontsize=8.5, va='top', ha='left',
            bbox=dict(boxstyle='round,pad=0.35', facecolor=SURFACE,
                      edgecolor=GRID_INK, linewidth=0.8))
    return guardar(fig, nombre)


def mapa_comparativa(gh, nh, gm, nm, nombre_modelo, nombre):
    vmax = max([g.max() for g in (gh, gm) if g is not None] or [1])
    fig, axes = plt.subplots(1, 2, figsize=(19, 6.2), facecolor=SURFACE)
    for ax, g, n, tit, cmap in [
            (axes[0], gh, nh, 'Humano (lo que grabaste entrenando)', CMAP_HUMANO),
            (axes[1], gm, nm, f'Modelo "{nombre_modelo}" (jugando)', CMAP_MODELO)]:
        _campo(ax)
        _pintar(ax, g, cmap, vmax=vmax)
        ax.set_title(tit, color=INK, fontsize=12, fontweight='bold', loc='left', pad=10)
        ax.text(0.012, 0.965, f'n = {n:,} frames', transform=ax.transAxes,
                color=INK_2, fontsize=8.5, va='top', ha='left',
                bbox=dict(boxstyle='round,pad=0.35', facecolor=SURFACE,
                          edgecolor=GRID_INK, linewidth=0.8))
    fig.suptitle('Ocupacion del campo: humano vs modelo', color=INK,
                 fontsize=14, fontweight='bold', x=0.008, ha='left', y=1.035)
    fig.text(0.008, 0.985, 'Misma escala de color en los dos paneles.',
             color=INK_2, fontsize=9, ha='left')
    fig.tight_layout()
    return guardar(fig, nombre)


def mapa_diferencia(gh, gm, nombre_modelo, sim, nombre):
    if gh is None or gm is None:
        return None
    d = gm - gh
    lim = float(np.abs(d).max()) or 1.0
    fig, ax = plt.subplots(figsize=(11, 6.4), facecolor=SURFACE)
    _campo(ax)
    im = ax.imshow(d.T, extent=[X_MIN, X_MAX, Z_MIN, Z_MAX], origin='lower',
                   cmap=CMAP_DIF, aspect='auto', interpolation='bilinear',
                   vmin=-lim, vmax=lim, zorder=2)
    cb = fig.colorbar(im, ax=ax, shrink=0.75, pad=0.015, ticks=[-lim, 0, lim])
    cb.ax.set_yticklabels(['solo el humano', 'igual', 'solo el modelo'])
    cb.ax.tick_params(colors=INK_2, labelsize=8)
    cb.outline.set_edgecolor(GRID_INK)
    ax.set_title('Donde se separa el modelo de ti', color=INK, fontsize=13,
                 fontweight='bold', loc='left', pad=22)
    ax.text(0, 1.02,
            'Azul: zonas que pisabas tu y el modelo no.    '
            'Naranja: zonas del modelo que tu no pisabas.',
            transform=ax.transAxes, color=INK_2, fontsize=9, va='bottom')
    ax.text(0.012, 0.965, f'solapamiento {sim*100:.0f}%', transform=ax.transAxes,
            color=INK, fontsize=9.5, fontweight='bold', va='top', ha='left',
            bbox=dict(boxstyle='round,pad=0.35', facecolor=SURFACE,
                      edgecolor=GRID_INK, linewidth=0.8))
    return guardar(fig, nombre)


def mapa_acciones(df, xs, zs, titulo, cmap, nombre):
    cols = [c for c in ('Disparo', 'Pase', 'RoboK', 'RoboL') if c in df.columns]
    cols = [c for c in cols if df[c].sum() > 0]
    if not cols:
        return None
    fig, axes = plt.subplots(1, len(cols), figsize=(6.2 * len(cols), 5.4),
                             facecolor=SURFACE, squeeze=False)
    for ax, c in zip(axes[0], cols):
        m = df[c].values == 1
        _campo(ax)
        ax.scatter(xs[m], zs[m], s=42, color=cmap, alpha=0.75,
                   edgecolors=SURFACE, linewidths=1.2, zorder=4)
        ax.set_title(f'{c}   n = {int(m.sum())}', color=INK, fontsize=11,
                     fontweight='bold', loc='left', pad=8)
    fig.suptitle(titulo, color=INK, fontsize=13, fontweight='bold',
                 x=0.006, ha='left', y=1.04)
    fig.tight_layout()
    return guardar(fig, nombre)


def _nombre_desde_fichero(ai):
    """AIRecorder nombra los ficheros AIData_<modelo>_<fecha>.csv."""
    if ai is None or '_fichero' not in ai.columns:
        return None
    nombres = set()
    for f in ai['_fichero'].unique():
        partes = os.path.splitext(f)[0].split('_')
        # AIData + nombre(s) + 6 campos de fecha
        if len(partes) >= 8:
            nombres.add('_'.join(partes[1:-6]))
    return nombres.pop() if len(nombres) == 1 else None


def escala_de_referencia(human, gm, n_modelo):
    """
    Un solapamiento suelto no se puede interpretar. Esto le pone escala:

      SUELO  un jugador que se mueve al azar por el campo.
      TECHO  el propio humano contra sus otras sesiones, recortado al mismo
             numero de frames que tiene la grabacion del modelo (si no, el
             tamanyo de muestra explica la diferencia mas que el comportamiento).
    """
    sesiones = {}
    for f, d in human.groupby('_fichero'):
        x, z, _, _, _ = posiciones_humano(d)
        g, n, _ = rejilla(x, z)
        if g is not None and n > 300:
            sesiones[f] = (g, n, d)
    if len(sesiones) < 2:
        print('    (hacen falta al menos 2 sesiones humanas para la escala)')
        return

    def agregado(excluir=None):
        tot = sum(v[1] for k, v in sesiones.items() if k != excluir)
        return sum(v[0] * v[1] for k, v in sesiones.items() if k != excluir) / tot

    techo = []
    for k, (_, _, d) in sesiones.items():
        if len(d) < n_modelo:
            continue
        ref = agregado(excluir=k)
        for ini in range(0, len(d) - n_modelo + 1, n_modelo):
            x, z, _, _, _ = posiciones_humano(d.iloc[ini:ini + n_modelo])
            g, _, _ = rejilla(x, z)
            techo.append(similitud(ref, g))

    rng = np.random.default_rng(0)
    todo = agregado()
    suelo = []
    for _ in range(30):
        g, _, _ = rejilla(rng.uniform(PORTERIA_1[0], PORTERIA_2[0], n_modelo),
                          rng.uniform(Z_MIN, Z_MAX, n_modelo))
        suelo.append(similitud(todo, g))

    print(f'    SUELO   movimiento al azar        {np.mean(suelo)*100:5.1f}%')
    if gm is not None:
        print(f'    MODELO  el NPC                    {similitud(todo, gm)*100:5.1f}%')
    if techo:
        t = np.array(techo)
        print(f'    TECHO   tu contra ti mismo        {np.median(t)*100:5.1f}%   '
              f'[{t.min()*100:.0f}-{t.max()*100:.0f}] en {len(t)} trozos de {n_modelo} frames')
    else:
        print('    TECHO   ninguna sesion tuya llega a ese numero de frames')


# ================================================================== main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--modelo', default=None,
                    help='filtra los AIData_*.csv por nombre de modelo')
    ap.add_argument('--humano', default=None,
                    help='filtra los SoccerData_*.csv (p.ej. la sesion que entreno al modelo)')
    args = ap.parse_args()

    print('=' * 70)
    print('  MAPAS DE CALOR - humano de entrenamiento vs modelo en juego')
    print('=' * 70)

    print('\n[1/4] Grabaciones del humano (SoccerData_*.csv)')
    human = cargar('SoccerData_*.csv', 'Humano', args.humano, requeridas=COLUMNAS_HUMANO)
    if human is None:
        sys.exit('ERROR: no hay ningun SoccerData_*.csv.')
    if 'RelPorteriaRivalX' not in human.columns:
        sys.exit('ERROR: al CSV humano le faltan las columnas de porteria.')

    hx, hz, hbx, hbz, lado = posiciones_humano(human)
    print(f'    El humano ataca hacia {lado}.')

    print('\n[2/4] Grabaciones del modelo (AIData_*.csv)')
    ai = cargar('AIData_*.csv', 'Modelo', args.modelo)
    tiene_ia = ai is not None and 'AbsMyPosX' in ai.columns
    nombre_modelo = _nombre_desde_fichero(ai) or args.modelo or 'sin nombre'
    if tiene_ia:
        ax_, az_, abx, abz = posiciones_modelo(ai)
        if 'RelPorteriaRivalX' not in ai.columns:
            print('    AVISO: fichero en formato antiguo (solo posicion absoluta).')
            print('           No se puede saber hacia que porteria atacaba el NPC, asi')
            print('           que la comparacion supone que jugaba en el mismo sentido')
            print('           que tu. Las grabaciones nuevas ya lo guardan.')
        cols_acc = [c for c in ('Disparo', 'Pase', 'RoboK', 'RoboL') if c in ai.columns]
        if len(cols_acc) < 4:
            print('    AVISO: fichero sin columnas de accion (AIRecorder antiguo: las')
            print('           escribia siempre a 0). No habra mapa de acciones.')
        elif ai[cols_acc].to_numpy().sum() == 0:
            print('    AVISO: el modelo no ejecuto NINGUNA accion en toda la partida.')
            print('           El fichero es correcto y las columnas estan; simplemente')
            print('           no hay nada que dibujar. Mira la distancia al balon: si el')
            print('           modelo no se acerca, sus puertas nunca se abren.')
    else:
        print('    No hay datos del modelo todavia.')

    print('\n[3/4] Calculando rejillas')
    gh, nh, _ = rejilla(hx, hz)
    ghb, _, _ = rejilla(hbx, hbz)
    gm = gmb = None
    nm = 0
    if tiene_ia:
        gm, nm, _ = rejilla(ax_, az_)
        gmb, _, _ = rejilla(abx, abz)
    sim_j = similitud(gh, gm)
    sim_p = similitud(ghb, gmb)
    if tiene_ia:
        print(f'    Solapamiento de posiciones jugador : {sim_j*100:5.1f}%')
        print(f'    Solapamiento de posiciones pelota  : {sim_p*100:5.1f}%')

    if tiene_ia and gm is not None:
        print('\n    Escala de referencia (el 66% de antes no significaba nada solo):')
        escala_de_referencia(human, gm, nm)

    print('\n[4/4] Generando imagenes')
    mapa_simple(hx, hz, 'Donde estuvo el jugador humano',
                'Grabaciones de entrenamiento.', CMAP_HUMANO,
                'heatmap_humano_jugador.png')
    mapa_simple(hbx, hbz, 'Donde estuvo la pelota (sesiones del humano)',
                'Grabaciones de entrenamiento.', CMAP_HUMANO,
                'heatmap_humano_pelota.png')
    mapa_acciones(human, hx, hz, 'Desde donde actua el humano',
                  AZUL, 'heatmap_humano_acciones.png')

    if tiene_ia:
        mapa_simple(ax_, az_, f'Donde estuvo el modelo "{nombre_modelo}"',
                    'Partida jugada por el NPC.', CMAP_MODELO,
                    'heatmap_modelo_jugador.png')
        mapa_comparativa(gh, nh, gm, nm, nombre_modelo,
                         'heatmap_comparativa_jugador.png')
        mapa_diferencia(gh, gm, nombre_modelo, sim_j,
                        'heatmap_diferencia_jugador.png')
        mapa_comparativa(ghb, len(hbx), gmb, len(abx), nombre_modelo,
                         'heatmap_comparativa_pelota.png')
        mapa_acciones(ai, ax_, az_, f'Desde donde actua el modelo "{nombre_modelo}"',
                      NARANJA, 'heatmap_modelo_acciones.png')

    print(f'\nListo. Todo en {OUTPUT_DIR}')
    if not tiene_ia:
        print('\nPara la comparativa: juega una partida con el modelo aplicado y')
        print('vuelve a ejecutar este script.')


if __name__ == '__main__':
    main()
