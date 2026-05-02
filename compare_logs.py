"""
compare_logs.py — Compara las métricas de los 3 modelos de entrenamiento.

Uso:
    cd TFG-NPC
    python compare_logs.py

Busca automáticamente el log más reciente de cada modelo en:
    PythonTrainingLegacy/logs/train_legacy_*.txt
    PythonTraining/logs/train_fsm_*.txt
    PythonTraining/logs/train_multimodel_*.txt
"""

import os
import re
import glob
from pathlib import Path

# ── Rutas de logs (relativas a la ubicación de este script) ──────────────────
BASE = Path(__file__).parent
LOG_PATTERNS = {
    'Legacy':     BASE / 'PythonTrainingLegacy' / 'logs' / 'train_legacy_*.txt',
    'FSM':        BASE / 'PythonTraining'        / 'logs' / 'train_fsm_*.txt',
    'MultiModel': BASE / 'PythonTraining'        / 'logs' / 'train_multimodel_*.txt',
}

# ── Métricas a extraer y su etiqueta para la tabla ───────────────────────────
# Cada entrada: (clave en METRICS_BLOCK, etiqueta en tabla, ¿es porcentaje?)
COMMON_METRICS = [
    ('ACC_MOV',   'Acc Mov %',   True),
    ('PREC_MOV',  'Prec Mov %',  True),
    ('REC_MOV',   'Rec Mov %',   True),
    ('F1_MOV',    'F1 Mov %',    True),
    ('LOSS_FINAL','Loss',        False),
]
LEGACY_EXTRA = [
    ('ACC_SHOOT',  'Acc Shoot %',  True),
    ('PREC_SHOOT', 'Prec Shoot %', True),
    ('REC_SHOOT',  'Rec Shoot %',  True),
    ('F1_SHOOT',   'F1 Shoot %',   True),
    ('ACC_PASS',   'Acc Pass %',   True),
    ('PREC_PASS',  'Prec Pass %',  True),
    ('REC_PASS',   'Rec Pass %',   True),
    ('F1_PASS',    'F1 Pass %',    True),
]
FSM_EXTRA = [
    ('ACC_STATE',    'Acc State %',    True),
    ('PREC_STATE',   'Prec State %',   True),
    ('REC_STATE',    'Rec State %',    True),
    ('F1_STATE',     'F1 State %',     True),
    ('PREC_STATE_W', 'Prec State W%',  True),
    ('REC_STATE_W',  'Rec State W%',   True),
    ('F1_STATE_W',   'F1 State W%',    True),
]
MULTIMODEL_SUBS = ['RECOVER', 'APPROACH', 'PASS', 'SHOOT']


# ── Funciones ─────────────────────────────────────────────────────────────────
def find_latest_log(pattern):
    files = sorted(glob.glob(str(pattern)))
    return files[-1] if files else None


def parse_metrics_block(log_path):
    """Extrae el bloque [METRICS_START]...[METRICS_END] del log."""
    try:
        with open(log_path, encoding='utf-8', errors='replace') as f:
            content = f.read()
    except Exception as e:
        print(f"  ERROR leyendo {log_path}: {e}")
        return {}

    match = re.search(r'\[METRICS_START\](.*?)\[METRICS_END\]', content, re.DOTALL)
    if not match:
        return {}

    metrics = {}
    for line in match.group(1).strip().splitlines():
        line = line.strip()
        if '=' in line:
            key, _, val = line.partition('=')
            try:
                metrics[key.strip()] = float(val.strip())
            except ValueError:
                metrics[key.strip()] = val.strip()
    return metrics


def fmt(value, is_pct):
    if isinstance(value, float):
        return f"{value:.1f}%" if is_pct else f"{value:.4f}"
    return str(value) if value else '—'


def print_section(title):
    print(f"\n{'═'*62}")
    print(f"  {title}")
    print(f"{'═'*62}")


def row(label, legacy_v, fsm_v, multi_v, is_pct=True):
    lv = fmt(legacy_v, is_pct)  if legacy_v is not None else '—'
    fv = fmt(fsm_v,    is_pct)  if fsm_v    is not None else 'N/A'
    mv = fmt(multi_v,  is_pct)  if multi_v  is not None else 'N/A'
    print(f"  {label:<20}  {lv:>10}  {fv:>10}  {mv:>10}")


# ── Main ──────────────────────────────────────────────────────────────────────
def main():
    print("╔══════════════════════════════════════════════════════════════╗")
    print("║        COMPARATIVA DE MÉTRICAS — TFG-NPC Soccer AI          ║")
    print("╚══════════════════════════════════════════════════════════════╝")

    # Buscar logs
    logs = {}
    for name, pattern in LOG_PATTERNS.items():
        path = find_latest_log(pattern)
        if path:
            print(f"  [{name}] → {os.path.basename(path)}")
            logs[name] = path
        else:
            print(f"  [{name}] → ⚠️  No se encontró ningún log en {pattern}")
            logs[name] = None

    # Parsear métricas
    metrics = {}
    for name, path in logs.items():
        metrics[name] = parse_metrics_block(path) if path else {}

    L = metrics.get('Legacy',     {})
    F = metrics.get('FSM',        {})
    M = metrics.get('MultiModel', {})

    # ── TABLA 1: Métricas comunes (movimiento + loss) ────────────────────────
    print_section("MOVIMIENTO — Acc / Prec / Rec / F1 / Loss")
    print(f"  {'Métrica':<20}  {'Legacy':>10}  {'FSM':>10}  {'MultiModel':>10}")
    print(f"  {'-'*20}  {'-'*10}  {'-'*10}  {'-'*10}")

    # MultiModel usa la media de los 4 sub-modelos para movimiento
    mm_acc = mm_prec = mm_rec = mm_f1 = mm_loss = None
    if M:
        accs   = [M.get(f'ACC_MOV_{s}')  for s in MULTIMODEL_SUBS if M.get(f'ACC_MOV_{s}')  is not None]
        precs  = [M.get(f'PREC_MOV_{s}') for s in MULTIMODEL_SUBS if M.get(f'PREC_MOV_{s}') is not None]
        recs   = [M.get(f'REC_MOV_{s}')  for s in MULTIMODEL_SUBS if M.get(f'REC_MOV_{s}')  is not None]
        f1s    = [M.get(f'F1_MOV_{s}')   for s in MULTIMODEL_SUBS if M.get(f'F1_MOV_{s}')   is not None]
        losses = [M.get(f'LOSS_{s}')      for s in MULTIMODEL_SUBS if M.get(f'LOSS_{s}')      is not None]
        mm_acc  = sum(accs)   / len(accs)   if accs   else None
        mm_prec = sum(precs)  / len(precs)  if precs  else None
        mm_rec  = sum(recs)   / len(recs)   if recs   else None
        mm_f1   = sum(f1s)    / len(f1s)    if f1s    else None
        mm_loss = sum(losses) / len(losses) if losses else None

    row('Acc Mov %',  L.get('ACC_MOV'),  F.get('ACC_MOV'),  mm_acc,  True)
    row('Prec Mov %', L.get('PREC_MOV'), F.get('PREC_MOV'), mm_prec, True)
    row('Rec Mov %',  L.get('REC_MOV'),  F.get('REC_MOV'),  mm_rec,  True)
    row('F1 Mov %',   L.get('F1_MOV'),   F.get('F1_MOV'),   mm_f1,   True)
    row('Loss',       L.get('LOSS_FINAL'),F.get('LOSS_FINAL'),mm_loss, False)

    # ── TABLA 2: Acciones (Disparo / Pase) — Solo Legacy ────────────────────
    print_section("ACCIONES — Disparo / Pase  (solo modelo Legacy)")
    print(f"  {'Métrica':<20}  {'Legacy':>10}  {'FSM':>10}  {'MultiModel':>10}")
    print(f"  {'-'*20}  {'-'*10}  {'-'*10}  {'-'*10}")
    for key, label, is_pct in LEGACY_EXTRA:
        row(label, L.get(key), None, None, is_pct)

    # ── TABLA 3: Estado FSM ──────────────────────────────────────────────────
    print_section("ESTADO FSM  (solo modelo FSM)")
    print(f"  {'Métrica':<20}  {'Legacy':>10}  {'FSM':>10}  {'MultiModel':>10}")
    print(f"  {'-'*20}  {'-'*10}  {'-'*10}  {'-'*10}")
    for key, label, is_pct in FSM_EXTRA:
        row(label, None, F.get(key), None, is_pct)

    # ── TABLA 4: Sub-modelos MultiModel ─────────────────────────────────────
    if M:
        print_section("SUB-MODELOS MultiModel — Movimiento por comportamiento")
        print(f"  {'Sub-modelo':<12}  {'Acc %':>7}  {'Prec %':>7}  {'Rec %':>7}  {'F1 %':>7}  {'Loss':>8}")
        print(f"  {'-'*12}  {'-'*7}  {'-'*7}  {'-'*7}  {'-'*7}  {'-'*8}")
        for s in MULTIMODEL_SUBS:
            a = M.get(f'ACC_MOV_{s}');  p = M.get(f'PREC_MOV_{s}')
            r = M.get(f'REC_MOV_{s}');  f = M.get(f'F1_MOV_{s}')
            l = M.get(f'LOSS_{s}')
            av = f"{a:.1f}%" if a is not None else '—'
            pv = f"{p:.1f}%" if p is not None else '—'
            rv = f"{r:.1f}%" if r is not None else '—'
            fv = f"{f:.1f}%" if f is not None else '—'
            lv = f"{l:.4f}"  if l is not None else '—'
            print(f"  {s:<12}  {av:>7}  {pv:>7}  {rv:>7}  {fv:>7}  {lv:>8}")

    # ── RESUMEN EJECUTIVO ────────────────────────────────────────────────────
    print_section("RESUMEN EJECUTIVO")
    winner = None
    best_f1 = -1
    for name, key in [('Legacy','F1_MOV'), ('FSM','F1_MOV')]:
        val = metrics[name].get(key)
        if val is not None and val > best_f1:
            best_f1, winner = val, name
    if mm_f1 is not None and mm_f1 > best_f1:
        best_f1, winner = mm_f1, 'MultiModel (media)'

    print(f"  Mejor F1 Movimiento : {winner}  ({best_f1:.1f}%)")

    ts_legacy = L.get('TIMESTAMP', '—')
    ts_fsm    = F.get('TIMESTAMP', '—')
    ts_multi  = M.get('TIMESTAMP', '—')
    print(f"  Legacy timestamp    : {ts_legacy}")
    print(f"  FSM    timestamp    : {ts_fsm}")
    print(f"  Multi  timestamp    : {ts_multi}")
    print()


if __name__ == '__main__':
    main()
