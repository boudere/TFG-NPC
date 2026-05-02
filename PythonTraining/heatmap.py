"""
heatmap.py — Mapas de calor: Jugador humano vs IA

Uso:
    cd TFG-NPC
    python PythonTraining\heatmap.py

Genera los mapas del entrenamiento humano (SoccerData_*.csv)
y, si existen datos de la IA (AIData_*.csv), añade la comparativa
Humano vs IA en un PNG lado a lado.

Salida en Assets/heatmaps/
"""

import glob
import os
import sys
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
import matplotlib.patches as patches
from matplotlib.colors import LinearSegmentedColormap
from pathlib import Path

# ── Rutas (siempre relativas al script, independiente de CWD) ────────────────
_HERE       = Path(__file__).parent
_ASSETS     = (_HERE / '../Assets').resolve()
OUTPUT_DIR  = str(_ASSETS / 'heatmaps')

HUMAN_PATTERN = str(_ASSETS / 'SoccerData_*.csv')
AI_PATTERN    = str(_ASSETS / 'AIData_*.csv')

# ── Dimensiones del campo (unidades Unity) ───────────────────────────────────
FIELD_X_MIN, FIELD_X_MAX = -1100, 700
FIELD_Z_MIN, FIELD_Z_MAX = -350, 350
GRID_W, GRID_H = 120, 70

# ── Helpers ──────────────────────────────────────────────────────────────────
def load_csvs(pattern, label='datos'):
    """Carga y concatena todos los CSVs que coincidan con el patrón."""
    files = sorted(glob.glob(pattern))
    if not files:
        return None

    frames = []
    for f in files:
        df = pd.read_csv(f)
        # Filtrar spawn noise (frames con aliados estáticos en origen)
        if 'Aliado1DirX' in df.columns:
            noise = ((df['Aliado1DirX'] == 0) & (df['Aliado1DirZ'] == 0) &
                     (df['Aliado2DirX'] == 0) & (df['Aliado2DirZ'] == 0))
            df = df[~noise]
        frames.append(df)
        print(f"  [{label}] {os.path.basename(f)}: {len(df)} filas")

    raw = pd.concat(frames, ignore_index=True)
    print(f"  [{label}] Total: {len(raw)} filas en {len(files)} archivo(s)\n")
    return raw


def _build_grid(xs, zs):
    """Construye y suaviza una grilla 2D normalizada."""
    from scipy.ndimage import gaussian_filter
    mask = ((xs >= FIELD_X_MIN) & (xs <= FIELD_X_MAX) &
            (zs >= FIELD_Z_MIN) & (zs <= FIELD_Z_MAX))
    xs, zs = xs[mask], zs[mask]
    if len(xs) == 0:
        return None, 0
    h, _, _ = np.histogram2d(
        xs, zs, bins=[GRID_W, GRID_H],
        range=[[FIELD_X_MIN, FIELD_X_MAX], [FIELD_Z_MIN, FIELD_Z_MAX]]
    )
    h = gaussian_filter(h, sigma=2)
    if h.max() > 0:
        h /= h.max()
    return h, len(xs)


def _draw_field(ax):
    """Dibuja el campo de fútbol de fondo."""
    ax.set_facecolor('#0d0d1a')
    campo = patches.Rectangle(
        (FIELD_X_MIN, FIELD_Z_MIN),
        FIELD_X_MAX - FIELD_X_MIN, FIELD_Z_MAX - FIELD_Z_MIN,
        linewidth=2, edgecolor='#4CAF50', facecolor='#1b5e20',
        alpha=0.25, zorder=0
    )
    ax.add_patch(campo)
    ax.axvline(0, color='#4CAF50', alpha=0.35, linewidth=1, linestyle='--')
    ax.axhline(0, color='#4CAF50', alpha=0.35, linewidth=1, linestyle='--')
    # Porterías
    for gx in [FIELD_X_MIN + 30, FIELD_X_MAX - 30]:
        ax.add_patch(patches.Rectangle(
            (gx - 15, -70), 30, 140,
            linewidth=1.5, edgecolor='white', facecolor='none',
            alpha=0.5, zorder=1
        ))


def make_single_heatmap(xs, zs, title, filename, cmap='hot'):
    """Genera un único heatmap y lo guarda en disco."""
    grid, n = _build_grid(xs, zs)
    if grid is None:
        print(f"  AVISO: sin datos para '{title}' — se omite.")
        return

    colors_def = [(0,0,0,0),(0.2,0,0.5,0.4),(1,0.2,0,0.8),(1,1,0,1)]
    cmap_obj   = LinearSegmentedColormap.from_list('soccer', colors_def)

    fig, ax = plt.subplots(figsize=(14, 8), facecolor='#1a1a2e')
    _draw_field(ax)

    extent = [FIELD_X_MIN, FIELD_X_MAX, FIELD_Z_MIN, FIELD_Z_MAX]
    im = ax.imshow(grid.T, extent=extent, origin='lower',
                   cmap=cmap_obj, aspect='auto', interpolation='bilinear',
                   vmin=0, vmax=1, zorder=2)

    cbar = fig.colorbar(im, ax=ax, shrink=0.8, pad=0.02)
    cbar.set_label('Densidad relativa', color='white', fontsize=10)
    plt.setp(cbar.ax.yaxis.get_ticklabels(), color='white')
    cbar.ax.yaxis.set_tick_params(color='white')

    ax.set_title(title, color='white', fontsize=14, fontweight='bold', pad=12)
    ax.set_xlabel('X (campo)', color='white', fontsize=10)
    ax.set_ylabel('Z (campo)', color='white', fontsize=10)
    ax.tick_params(colors='white')
    for spine in ax.spines.values():
        spine.set_edgecolor('#4CAF50')
    ax.text(0.02, 0.97, f"n = {n:,} puntos",
            transform=ax.transAxes, color='white', fontsize=9,
            verticalalignment='top',
            bbox=dict(boxstyle='round,pad=0.3', facecolor='black', alpha=0.5))

    os.makedirs(OUTPUT_DIR, exist_ok=True)
    fig.savefig(filename, dpi=150, bbox_inches='tight',
                facecolor=fig.get_facecolor())
    plt.close(fig)
    print(f"  Guardado: {os.path.basename(filename)}")


def make_comparison(xs_a, zs_a, label_a, color_a,
                    xs_b, zs_b, label_b, color_b,
                    title, filename):
    """Genera un PNG con dos heatmaps lado a lado para comparar."""
    grid_a, n_a = _build_grid(xs_a, zs_a)
    grid_b, n_b = _build_grid(xs_b, zs_b)

    if grid_a is None and grid_b is None:
        print(f"  AVISO: sin datos para comparativa '{title}'.")
        return

    fig, axes = plt.subplots(1, 2, figsize=(22, 8), facecolor='#1a1a2e')
    extent = [FIELD_X_MIN, FIELD_X_MAX, FIELD_Z_MIN, FIELD_Z_MAX]

    for ax, grid, n, label, color in [
        (axes[0], grid_a, n_a, label_a, color_a),
        (axes[1], grid_b, n_b, label_b, color_b),
    ]:
        _draw_field(ax)
        if grid is not None:
            cmap_obj = LinearSegmentedColormap.from_list(
                'cmp', [(0,0,0,0),(0.1,0,0.4,0.4),(0.9,0.2,0,0.85),(1,1,0,1)]
            ) if color == 'hot' else LinearSegmentedColormap.from_list(
                'cmp', [(0,0,0,0),(0,0.2,0.5,0.4),(0,0.7,1,0.85),(0,1,0.8,1)]
            )
            ax.imshow(grid.T, extent=extent, origin='lower',
                      cmap=cmap_obj, aspect='auto', interpolation='bilinear',
                      vmin=0, vmax=1, zorder=2)
        ax.set_title(f"{label}\n(n = {n:,})", color='white',
                     fontsize=13, fontweight='bold')
        ax.set_xlabel('X (campo)', color='white', fontsize=10)
        ax.set_ylabel('Z (campo)', color='white', fontsize=10)
        ax.tick_params(colors='white')
        for spine in ax.spines.values():
            spine.set_edgecolor('#4CAF50')

    fig.suptitle(title, color='white', fontsize=15, fontweight='bold', y=1.01)
    fig.tight_layout()
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    fig.savefig(filename, dpi=150, bbox_inches='tight',
                facecolor=fig.get_facecolor())
    plt.close(fig)
    print(f"  Guardado: {os.path.basename(filename)}")


# ── Main ──────────────────────────────────────────────────────────────────────
def main():
    print("=" * 58)
    print("  GENERADOR DE HEATMAPS — TFG-NPC Soccer AI")
    print("=" * 58)

    # ─── Cargar datasets ────────────────────────────────────────────────────
    print("\n[1/3] Cargando datos de entrenamiento (humano)...")
    human = load_csvs(HUMAN_PATTERN, 'Humano')
    if human is None:
        print(f"ERROR: no hay CSVs en {HUMAN_PATTERN}")
        sys.exit(1)

    if 'AbsMyPosX' not in human.columns:
        print("ERROR: el CSV humano no tiene columnas AbsMyPosX/Z.")
        print("Graba una sesión nueva con el Recorder.cs actualizado.")
        sys.exit(1)

    print("[2/3] Buscando datos de la IA (AIData_*.csv)...")
    ai = load_csvs(AI_PATTERN, 'IA')
    has_ai = ai is not None and 'AbsMyPosX' in ai.columns

    if not has_ai:
        print("  ⚠  No hay datos de IA todavía.")
        print("     Añade AIRecorder.cs al agente y juega una partida.\n")

    # ─── Heatmaps individuales del HUMANO ───────────────────────────────────
    print("[3/3] Generando imágenes...\n")

    make_single_heatmap(
        human['AbsMyPosX'].values, human['AbsMyPosZ'].values,
        title='Posición del JUGADOR (entrenamiento humano)',
        filename=os.path.join(OUTPUT_DIR, 'heatmap_humano_jugador.png')
    )
    make_single_heatmap(
        human['AbsBallPosX'].values, human['AbsBallPosZ'].values,
        title='Posición de la PELOTA (entrenamiento humano)',
        filename=os.path.join(OUTPUT_DIR, 'heatmap_humano_pelota.png')
    )

    # Disparos y pases del humano
    for col, fname, label in [
        ('Disparo', 'heatmap_humano_disparos.png', 'disparos del humano'),
        ('Pase',    'heatmap_humano_pases.png',    'pases del humano'),
    ]:
        if col in human.columns:
            sub = human[human[col] == 1]
            if len(sub) > 0:
                make_single_heatmap(
                    sub['AbsMyPosX'].values, sub['AbsMyPosZ'].values,
                    title=f'Posiciones de {label} (n={len(sub)})',
                    filename=os.path.join(OUTPUT_DIR, fname)
                )

    # ─── Heatmaps de la IA ──────────────────────────────────────────────────
    if has_ai:
        make_single_heatmap(
            ai['AbsMyPosX'].values, ai['AbsMyPosZ'].values,
            title='Posición de la IA durante el juego',
            filename=os.path.join(OUTPUT_DIR, 'heatmap_ia_jugador.png')
        )

        # ─── COMPARATIVA HUMANO vs IA ────────────────────────────────────────
        make_comparison(
            human['AbsMyPosX'].values, human['AbsMyPosZ'].values,
            label_a='🧑 HUMANO (entrenamiento)', color_a='hot',
            xs_b=ai['AbsMyPosX'].values, zs_b=ai['AbsMyPosZ'].values,
            label_b='🤖 IA (comportamiento)', color_b='cool',
            title='Comparativa de posiciones: Humano vs IA',
            filename=os.path.join(OUTPUT_DIR, 'heatmap_comparativa_humano_vs_ia.png')
        )

        # Comparativa pelota
        make_comparison(
            human['AbsBallPosX'].values, human['AbsBallPosZ'].values,
            label_a='⚽ Pelota (sesión humano)', color_a='hot',
            xs_b=ai['AbsBallPosX'].values, zs_b=ai['AbsBallPosZ'].values,
            label_b='⚽ Pelota (sesión IA)', color_b='cool',
            title='Comparativa de la pelota: Humano vs IA',
            filename=os.path.join(OUTPUT_DIR, 'heatmap_comparativa_pelota.png')
        )

    print(f"\n✓ Todo guardado en: {OUTPUT_DIR}")
    if not has_ai:
        print("\nPróximo paso:")
        print("  1. Añade AIRecorder.cs al agente en Unity.")
        print("  2. Juega una partida con la IA activa.")
        print("  3. Cierra el juego (se guarda AIData_[timestamp].csv).")
        print("  4. Vuelve a ejecutar: python PythonTraining\\heatmap.py")


if __name__ == '__main__':
    main()
