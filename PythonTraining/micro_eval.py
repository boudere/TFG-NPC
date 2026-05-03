"""
micro_eval.py — Evaluación de micro-comportamiento táctico.

Compara las decisiones del modelo neural (Legacy) con las del humano
en el espacio de variables relativas (contexto táctico inmediato).

Requiere: pip install onnxruntime matplotlib scipy

Uso:
    cd TFG-NPC
    python PythonTraining\micro_eval.py
"""

import glob, json, os, sys
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
from matplotlib.colors import LinearSegmentedColormap
from pathlib import Path
from scipy.ndimage import gaussian_filter

try:
    import onnxruntime as ort
except ImportError:
    print("ERROR: instala onnxruntime:  pip install onnxruntime")
    sys.exit(1)

# ── Rutas ──────────────────────────────────────────────────────────────────────
_HERE   = Path(__file__).parent
_ASSETS = (_HERE / '../Assets').resolve()

ONNX_PATH   = str(_ASSETS / 'SoccerModel_Legacy.onnx')
SCALER_PATH = str(_ASSETS / 'scaler_legacy.json')
CSV_PATTERN = str(_ASSETS / 'SoccerData_*.csv')
OUTPUT_DIR  = str(_ASSETS / 'heatmaps' / 'micro_eval')

FEATURE_COLS = [
    'RelPorteriaRivalX', 'RelPorteriaRivalZ',
    'RelPorteriaPropiaX', 'RelPorteriaPropiaZ',
    'TienePelota',
    'RelPelotaX', 'RelPelotaZ', 'DistPelota',
    'TienePelotaEquipo',
    'DistPorteriaContraria', 'DistPorteriaPropia',
    'PuntuacionPropia', 'PuntuacionContraria',
    'DistPelotaPorteriaPropia',
    'DistAliadoCercano', 'DistEnemigoCercano',
    'RelAliado1PosX', 'RelAliado1PosZ', 'Aliado1DirX', 'Aliado1DirZ',
    'RelAliado2PosX', 'RelAliado2PosZ', 'Aliado2DirX', 'Aliado2DirZ',
    'RelAliado3PosX', 'RelAliado3PosZ', 'Aliado3DirX', 'Aliado3DirZ',
    'RelEnemigo1PosX', 'RelEnemigo1PosZ', 'Enemigo1DirX', 'Enemigo1DirZ',
    'RelEnemigo2PosX', 'RelEnemigo2PosZ', 'Enemigo2DirX', 'Enemigo2DirZ',
    'RelEnemigo3PosX', 'RelEnemigo3PosZ', 'Enemigo3DirX', 'Enemigo3DirZ',
]

DARK_BG = '#0f0f1a'

# ── Helpers ───────────────────────────────────────────────────────────────────
def style_ax(ax, title, xlabel, ylabel):
    ax.set_facecolor('#0d0d1a')
    ax.set_title(title, color='white', fontsize=12, fontweight='bold', pad=10)
    ax.set_xlabel(xlabel, color='white', fontsize=9)
    ax.set_ylabel(ylabel, color='white', fontsize=9)
    ax.tick_params(colors='white')
    for sp in ax.spines.values():
        sp.set_edgecolor('#334')


def load_data():
    files = sorted(glob.glob(CSV_PATTERN))
    frames = []
    for f in files:
        df = pd.read_csv(f)
        mask = ((df['Aliado1DirX'] == 0) & (df['Aliado1DirZ'] == 0) &
                (df['Aliado2DirX'] == 0) & (df['Aliado2DirZ'] == 0))
        frames.append(df[~mask])
    raw = pd.concat(frames, ignore_index=True)
    print(f"  Dataset: {len(raw)} filas de {len(files)} archivo(s)")
    return raw


def load_model_and_scaler():
    if not os.path.exists(ONNX_PATH):
        print(f"ERROR: no se encuentra el modelo ONNX en {ONNX_PATH}")
        sys.exit(1)
    session = ort.InferenceSession(ONNX_PATH)

    scaler = {'mean': np.zeros(40), 'std': np.ones(40)}
    if os.path.exists(SCALER_PATH):
        with open(SCALER_PATH) as f:
            s = json.load(f)
        scaler['mean'] = np.array(s['mean'], dtype=np.float32)
        scaler['std']  = np.array(s['std'],  dtype=np.float32)
        scaler['std'][scaler['std'] == 0] = 1.0
    else:
        print("  AVISO: scaler_legacy.json no encontrado — se usarán features sin normalizar")

    return session, scaler


def predict_batch(session, scaler, X_raw):
    X = (X_raw - scaler['mean']) / scaler['std']
    X = np.clip(X, -3, 3).astype(np.float32)
    inputs = {session.get_inputs()[0].name: X}
    outputs = session.run(None, inputs)
    # outputs[0] = movement (InputX, InputZ), outputs[1] = action logits (Disparo, Pase)
    movement = outputs[0]                          # [N, 2]
    action_logits = outputs[1] if len(outputs) > 1 else np.zeros((len(X), 2))
    actions = (action_logits > 0).astype(int)
    return movement, actions


def discretize(v, thr=0.3):
    return np.where(v > thr, 1, np.where(v < -thr, -1, 0))


# ── Plot 1: Mapa de acuerdo de movimiento en espacio RelPelota ────────────────
def plot_movement_agreement(df, pred_mov, out_dir):
    """
    En el espacio (RelPelotaX, RelPelotaZ), muestra qué % de frames
    el modelo predice la misma dirección de movimiento que el humano.
    Verde = alto acuerdo, Rojo = desacuerdo.
    """
    RBX = df['RelPelotaX'].values
    RBZ = df['RelPelotaZ'].values

    hum_x = discretize(df['InputX'].values)
    hum_z = discretize(df['InputZ'].values)
    mod_x = discretize(pred_mov[:, 0])
    mod_z = discretize(pred_mov[:, 1])

    agree = ((hum_x == mod_x) & (hum_z == mod_z)).astype(float)

    # Limitar al rango relevante
    xlim, zlim = 500, 500
    mask = (np.abs(RBX) < xlim) & (np.abs(RBZ) < zlim)
    RBX, RBZ, agree = RBX[mask], RBZ[mask], agree[mask]

    BINS = 30
    xedges = np.linspace(-xlim, xlim, BINS + 1)
    zedges = np.linspace(-zlim, zlim, BINS + 1)

    agree_sum, _, _ = np.histogram2d(RBX, RBZ, bins=[xedges, zedges], weights=agree)
    count,     _, _ = np.histogram2d(RBX, RBZ, bins=[xedges, zedges])
    rate = np.where(count > 0, agree_sum / count, np.nan)

    cmap = LinearSegmentedColormap.from_list('agree',
        [(0.8, 0.1, 0.1), (1, 0.8, 0), (0.1, 0.7, 0.2)])

    fig, ax = plt.subplots(figsize=(10, 9), facecolor=DARK_BG)
    im = ax.imshow(rate.T, extent=[-xlim, xlim, -zlim, zlim],
                   origin='lower', cmap=cmap, aspect='auto',
                   vmin=0.4, vmax=1.0, interpolation='bilinear')

    # Referencia: círculos de distancia
    for r in [100, 250, 400]:
        circle = plt.Circle((0, 0), r, color='white', fill=False, alpha=0.15, linewidth=0.8)
        ax.add_patch(circle)

    ax.axvline(0, color='white', alpha=0.2, linewidth=0.8, linestyle='--')
    ax.axhline(0, color='white', alpha=0.2, linewidth=0.8, linestyle='--')
    ax.scatter([0], [0], s=180, color='yellow', zorder=5, label='Pelota (origen)')

    cbar = fig.colorbar(im, ax=ax, shrink=0.8)
    cbar.set_label('Acuerdo Humano-Modelo', color='white', fontsize=9)
    plt.setp(cbar.ax.yaxis.get_ticklabels(), color='white')

    overall = agree.mean()
    ax.text(0.02, 0.97, f"Acuerdo global: {overall*100:.1f}%",
            transform=ax.transAxes, color='white', fontsize=10,
            va='top', bbox=dict(facecolor='black', alpha=0.5, pad=4))

    style_ax(ax,
        'MICRO-COMPORTAMIENTO: Acuerdo de movimiento\nen espacio relativo a la pelota',
        'RelPelotaX (← izquierda | derecha →)',
        'RelPelotaZ (← atrás | adelante →)')
    ax.legend(loc='lower right', fontsize=8, labelcolor='white',
              facecolor='#111', edgecolor='#334')

    path = os.path.join(out_dir, 'micro_movement_agreement.png')
    fig.tight_layout()
    fig.savefig(path, dpi=150, bbox_inches='tight', facecolor=fig.get_facecolor())
    plt.close(fig)
    print(f"  Guardado: {os.path.basename(path)}  (acuerdo global: {overall*100:.1f}%)")
    return overall


# ── Plot 2: Zona de disparo en espacio RelPorteriaRival ───────────────────────
def plot_shoot_zones(df, pred_actions, out_dir):
    """
    Muestra desde qué posición relativa a la portería rival
    dispara el humano vs el modelo.
    """
    rpx = df['RelPorteriaRivalX'].values
    rpz = df['RelPorteriaRivalZ'].values

    hum_shoot = df['Disparo'].values == 1
    mod_shoot = pred_actions[:, 0] == 1

    fig, axes = plt.subplots(1, 2, figsize=(16, 7), facecolor=DARK_BG)

    for ax, mask, label, color in [
        (axes[0], hum_shoot, f'HUMANO  (n={hum_shoot.sum()})', '#FFD700'),
        (axes[1], mod_shoot, f'MODELO  (n={mod_shoot.sum()})', '#00BFFF'),
    ]:
        style_ax(ax, f'Zona de disparo — {label}',
                 'RelPorteriaRivalX', 'RelPorteriaRivalZ')
        ax.set_facecolor('#0d0d1a')

        if mask.sum() > 0:
            ax.scatter(rpx[mask], rpz[mask], s=60, color=color,
                       alpha=0.7, edgecolors='white', linewidths=0.5, zorder=3)

        # Marcar la portería
        ax.scatter([0], [0], s=300, marker='s', color='white',
                   zorder=5, label='Portería rival')
        ax.axvline(0, color='white', alpha=0.2, linewidth=0.8)
        ax.axhline(0, color='white', alpha=0.2, linewidth=0.8)

        for r in [100, 200, 350]:
            ax.add_patch(plt.Circle((0, 0), r, color=color,
                         fill=False, alpha=0.15, linewidth=0.8))
        ax.legend(fontsize=8, labelcolor='white', facecolor='#111', edgecolor='#334')

    fig.suptitle('MICRO-COMPORTAMIENTO: Zonas de disparo en espacio relativo a portería rival',
                 color='white', fontsize=13, fontweight='bold')
    fig.tight_layout()
    path = os.path.join(out_dir, 'micro_shoot_zones.png')
    fig.savefig(path, dpi=150, bbox_inches='tight', facecolor=fig.get_facecolor())
    plt.close(fig)
    print(f"  Guardado: {os.path.basename(path)}")


# ── Plot 3: Error angular del movimiento ──────────────────────────────────────
def plot_angular_error(df, pred_mov, out_dir):
    """
    Histograma del ángulo entre el vector de movimiento humano y el del modelo.
    0° = acuerdo perfecto, 180° = dirección opuesta.
    """
    hum_vec = np.column_stack([df['InputX'].values, df['InputZ'].values])
    mod_vec = pred_mov

    # Filtrar frames con movimiento real
    hum_norm = np.linalg.norm(hum_vec, axis=1)
    mod_norm = np.linalg.norm(mod_vec, axis=1)
    moving = (hum_norm > 0.1) & (mod_norm > 0.1)

    if moving.sum() == 0:
        print("  AVISO: sin frames con movimiento para calcular error angular")
        return

    h = hum_vec[moving] / hum_norm[moving, None]
    m = mod_vec[moving] / mod_norm[moving, None]
    cos_sim = np.clip((h * m).sum(axis=1), -1, 1)
    angles  = np.degrees(np.arccos(cos_sim))

    fig, ax = plt.subplots(figsize=(10, 6), facecolor=DARK_BG)
    ax.set_facecolor('#0d0d1a')

    bins = np.linspace(0, 180, 37)
    n, _, patches = ax.hist(angles, bins=bins, color='#4488FF', edgecolor='#223', alpha=0.85)

    # Colorear por zona
    for patch, left in zip(patches, bins[:-1]):
        if left < 45:
            patch.set_facecolor('#22CC55')   # verde: buen acuerdo
        elif left < 90:
            patch.set_facecolor('#FFCC00')   # amarillo
        else:
            patch.set_facecolor('#EE3333')   # rojo: desacuerdo

    ax.axvline(angles.mean(), color='white', linewidth=2, linestyle='--',
               label=f'Media: {angles.mean():.1f}°')
    ax.axvline(np.median(angles), color='cyan', linewidth=1.5, linestyle=':',
               label=f'Mediana: {np.median(angles):.1f}°')

    pct_45  = (angles < 45).mean() * 100
    pct_90  = (angles < 90).mean() * 100

    ax.text(0.98, 0.95,
            f"< 45°: {pct_45:.1f}%\n< 90°: {pct_90:.1f}%",
            transform=ax.transAxes, color='white', fontsize=10,
            ha='right', va='top', bbox=dict(facecolor='black', alpha=0.5, pad=4))

    style_ax(ax,
        'MICRO-COMPORTAMIENTO: Error angular de movimiento\n(0° = acuerdo perfecto, 180° = dirección opuesta)',
        'Error angular (grados)', 'Nº de frames')
    ax.legend(fontsize=9, labelcolor='white', facecolor='#111', edgecolor='#334')

    patches_leg = [
        mpatches.Patch(color='#22CC55', label='Buen acuerdo (<45°)'),
        mpatches.Patch(color='#FFCC00', label='Acuerdo parcial (45-90°)'),
        mpatches.Patch(color='#EE3333', label='Desacuerdo (>90°)'),
    ]
    ax.legend(handles=patches_leg, fontsize=8, labelcolor='white',
              facecolor='#111', edgecolor='#334', loc='upper right')

    path = os.path.join(out_dir, 'micro_angular_error.png')
    fig.tight_layout()
    fig.savefig(path, dpi=150, bbox_inches='tight', facecolor=fig.get_facecolor())
    plt.close(fig)
    print(f"  Guardado: {os.path.basename(path)}  "
          f"(error medio: {angles.mean():.1f}°, <45°: {pct_45:.1f}%)")


# ── Plot 4: Similitud por contexto táctico ────────────────────────────────────
def plot_context_similarity(df, pred_mov, pred_actions, out_dir):
    """
    Compara el acuerdo modelo-humano desglosado por contexto táctico:
    - Tengo la pelota
    - Mi equipo tiene la pelota
    - Rival tiene la pelota
    - Pelota libre
    - Cerca de la portería rival (dist < 200)
    - Lejos de la portería rival (dist > 400)
    """
    hum_x  = discretize(df['InputX'].values)
    hum_z  = discretize(df['InputZ'].values)
    mod_x  = discretize(pred_mov[:, 0])
    mod_z  = discretize(pred_mov[:, 1])
    mov_ok = (hum_x == mod_x) & (hum_z == mod_z)

    hum_shoot = df['Disparo'].values == 1
    mod_shoot = pred_actions[:, 0] == 1
    shoot_ok  = (hum_shoot == mod_shoot)

    # Definir contextos
    tp = df['TienePelota'].values == 1
    te = (df['TienePelotaEquipo'].values == 1) & ~tp
    tr = df['TienePelotaEquipo'].values == 2
    tl = df['TienePelotaEquipo'].values == 0
    cerca  = df['DistPorteriaContraria'].values < 200
    lejos  = df['DistPorteriaContraria'].values > 400

    contexts = {
        'Yo tengo\nla pelota':      tp,
        'Aliado tiene\nla pelota':  te,
        'Rival tiene\nla pelota':   tr,
        'Pelota\nlibre':            tl,
        'Cerca portería\nrival':    cerca,
        'Lejos portería\nrival':    lejos,
    }

    labels, mov_vals, shoot_vals, counts = [], [], [], []
    for label, mask in contexts.items():
        n = mask.sum()
        if n < 5:
            continue
        labels.append(label)
        counts.append(n)
        mov_vals.append(mov_ok[mask].mean() * 100)
        shoot_vals.append(shoot_ok[mask].mean() * 100)

    x = np.arange(len(labels))
    width = 0.38

    fig, ax = plt.subplots(figsize=(13, 7), facecolor=DARK_BG)
    ax.set_facecolor('#0d0d1a')

    bars1 = ax.bar(x - width/2, mov_vals,   width, color='#4488FF', label='Movimiento', alpha=0.85)
    bars2 = ax.bar(x + width/2, shoot_vals, width, color='#FF8844', label='Disparo',    alpha=0.85)

    for bars in [bars1, bars2]:
        for bar in bars:
            h = bar.get_height()
            ax.text(bar.get_x() + bar.get_width()/2, h + 0.8,
                    f'{h:.1f}%', ha='center', va='bottom', color='white', fontsize=8)

    # Nº de muestras por contexto
    for i, (n, lbl) in enumerate(zip(counts, labels)):
        ax.text(i, -6, f'n={n}', ha='center', color='#aaa', fontsize=7)

    ax.axhline(50, color='white', alpha=0.2, linewidth=0.8, linestyle='--')
    ax.set_xticks(x)
    ax.set_xticklabels(labels, color='white', fontsize=9)
    ax.set_ylim(0, 110)
    ax.set_yticks(range(0, 101, 20))

    style_ax(ax,
        'MICRO-COMPORTAMIENTO: Similitud Humano-Modelo por contexto táctico',
        'Contexto táctico', 'Acuerdo con el humano (%)')
    ax.legend(fontsize=9, labelcolor='white', facecolor='#111', edgecolor='#334')

    path = os.path.join(out_dir, 'micro_context_similarity.png')
    fig.tight_layout()
    fig.savefig(path, dpi=150, bbox_inches='tight', facecolor=fig.get_facecolor())
    plt.close(fig)
    print(f"  Guardado: {os.path.basename(path)}")


# ── Main ──────────────────────────────────────────────────────────────────────
def main():
    print("=" * 58)
    print("  EVALUACIÓN DE MICRO-COMPORTAMIENTO — TFG-NPC")
    print("=" * 58)

    os.makedirs(OUTPUT_DIR, exist_ok=True)

    print("\n[1] Cargando datos...")
    df = load_data()

    print("\n[2] Cargando modelo ONNX...")
    session, scaler = load_model_and_scaler()

    print("\n[3] Ejecutando inferencia...")
    X_raw = df[FEATURE_COLS].values.astype(np.float32)
    pred_mov, pred_actions = predict_batch(session, scaler, X_raw)
    print(f"  Predicciones: {len(pred_mov)} frames")
    print(f"  Disparos predichos: {pred_actions[:, 0].sum()}  |  Humano: {int(df['Disparo'].sum())}")
    print(f"  Pases predichos:    {pred_actions[:, 1].sum()}  |  Humano: {int(df['Pase'].sum())}")

    print("\n[4] Generando visualizaciones...\n")
    agreement = plot_movement_agreement(df, pred_mov, OUTPUT_DIR)
    plot_shoot_zones(df, pred_actions, OUTPUT_DIR)
    plot_angular_error(df, pred_mov, OUTPUT_DIR)
    plot_context_similarity(df, pred_mov, pred_actions, OUTPUT_DIR)

    print(f"\n{'='*58}")
    print(f"  Acuerdo global de movimiento: {agreement*100:.1f}%")
    print(f"  Imágenes en: {OUTPUT_DIR}")
    print(f"{'='*58}")


if __name__ == '__main__':
    main()
