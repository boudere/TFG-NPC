"""
arquitecturas.py - Las seis variantes comparadas en el TFG
==========================================================
Las definiciones se han copiado de los scripts originales del proyecto
(train_clasificacion_fnn.py, train.py, train_rnn.py, train_gru.py,
train_sliding.py, train_fsm.py) sin cambiar capas ni tamanyos, para que la
comparativa mida la arquitectura y no una reimplementacion distinta.

Unico cambio: la cabeza de accion se fija a 2 salidas (Disparo y Pase), que
es lo que soportaban todas en su version original. Asi ninguna sale
beneficiada por haberse escrito despues que las teclas K y L.
"""
import numpy as np
import torch
import torch.nn as nn

N_ACC = 2
N_MOV = 9


# ------------------------------------------------------- 1. FNN dual-head
class FNNDualHead(nn.Module):
    """Modelo base: tronco compartido y dos cabezas."""
    def __init__(self, input_size=40):
        super().__init__()
        self.backbone = nn.Sequential(
            nn.Linear(input_size, 128), nn.ReLU(), nn.Dropout(0.2),
            nn.Linear(128, 64), nn.ReLU(), nn.Dropout(0.2),
        )
        self.movement_head = nn.Linear(64, N_MOV)
        self.action_head = nn.Linear(64, N_ACC)

    def forward(self, x):
        h = self.backbone(x)
        return self.movement_head(h), self.action_head(h)


# ------------------------------------------------- 2. FNN sliding window
class FNNSlidingWindow(nn.Module):
    """Sin recurrencia: concatena el fotograma actual y el anterior (80 valores)."""
    def __init__(self, input_size=80):
        super().__init__()
        self.movement_branch = nn.Sequential(
            nn.Linear(input_size, 128), nn.ReLU(),
            nn.Linear(128, 64), nn.ReLU(), nn.Linear(64, N_MOV))
        self.action_branch = nn.Sequential(
            nn.Linear(input_size, 128), nn.ReLU(),
            nn.Linear(128, 64), nn.ReLU(), nn.Linear(64, N_ACC))

    def forward(self, x):
        return self.movement_branch(x), self.action_branch(x)


# ------------------------------------------------------------- 3. RNN
class CustomRNNCell(nn.Module):
    """Celda de Elman escrita a mano para que el grafo ONNX sea compatible
    con el Inference Engine de Unity."""
    def __init__(self, input_size, hidden_size):
        super().__init__()
        self.hidden_size = hidden_size
        self.ih = nn.Linear(input_size, hidden_size)
        self.hh = nn.Linear(hidden_size, hidden_size)

    def forward(self, x, hx):
        return torch.tanh(self.ih(x) + self.hh(hx))


class SoccerRNN(nn.Module):
    def __init__(self, input_size=40, hidden_size=64):
        super().__init__()
        self.hidden_size = hidden_size
        self.cell = CustomRNNCell(input_size, hidden_size)
        self.movement_head = nn.Linear(hidden_size, N_MOV)
        self.action_head = nn.Linear(hidden_size, N_ACC)

    def forward(self, x):                      # x: [B, T, 40]
        h = torch.zeros(x.size(0), self.hidden_size, device=x.device)
        for t in range(x.size(1)):
            h = self.cell(x[:, t, :], h)
        return self.movement_head(h), self.action_head(h)


# ------------------------------------------------------------- 4. GRU
class CustomGRUCell(nn.Module):
    """Celda GRU completa (puertas de reset y update) escrita a mano."""
    def __init__(self, input_size, hidden_size):
        super().__init__()
        self.hidden_size = hidden_size
        self.W_ir = nn.Linear(input_size, hidden_size)
        self.W_hr = nn.Linear(hidden_size, hidden_size, bias=False)
        self.W_iz = nn.Linear(input_size, hidden_size)
        self.W_hz = nn.Linear(hidden_size, hidden_size, bias=False)
        self.W_in = nn.Linear(input_size, hidden_size)
        self.W_hn = nn.Linear(hidden_size, hidden_size, bias=False)

    def forward(self, x, h):
        r = torch.sigmoid(self.W_ir(x) + self.W_hr(h))
        z = torch.sigmoid(self.W_iz(x) + self.W_hz(h))
        n = torch.tanh(self.W_in(x) + r * self.W_hn(h))
        return (1 - z) * n + z * h


class SoccerGRU(nn.Module):
    def __init__(self, input_size=40, hidden_size=64):
        super().__init__()
        self.hidden_size = hidden_size
        self.cell = CustomGRUCell(input_size, hidden_size)
        self.movement_branch = nn.Sequential(
            nn.Linear(hidden_size, 32), nn.ReLU(), nn.Linear(32, N_MOV))
        self.action_branch = nn.Sequential(
            nn.Linear(hidden_size, 32), nn.ReLU(), nn.Linear(32, N_ACC))

    def forward(self, x):                      # x: [B, T, 40]
        h = torch.zeros(x.size(0), self.hidden_size, device=x.device)
        for t in range(x.size(1)):
            h = self.cell(x[:, t, :], h)
        return self.movement_branch(h), self.action_branch(h)


# ------------------------------------------------------------- 5. FSM
class SoccerFSM(nn.Module):
    """
    No clasifica el movimiento en 9 clases: predice 4 estados tacticos
    (Defendiendo, Atacando, Pasando, Tirando) y regresa el movimiento
    continuo con Tanh. Pese al nombre no es una maquina de estados
    programada: es una red entrenada de forma supervisada para clasificar
    ese estado.
    """
    ESTADOS = ['Defendiendo', 'Atacando', 'Pasando', 'Tirando']

    def __init__(self, input_size=40, num_states=4):
        super().__init__()
        self.backbone = nn.Sequential(
            nn.Linear(input_size, 128), nn.ReLU(), nn.Dropout(0.2),
            nn.Linear(128, 64), nn.ReLU(),
        )
        self.state_head = nn.Linear(64, num_states)
        self.movement_head = nn.Sequential(nn.Linear(64, 2), nn.Tanh())

    def forward(self, x):
        h = self.backbone(x)
        return self.state_head(h), self.movement_head(h)


def estado_fsm(df):
    """Prioridad: Tirando > Pasando > Atacando > Defendiendo.
    Tirando y Pasando solo con balon, como en el script original."""
    e = np.zeros(len(df), dtype=int)
    tiene = df['TienePelota'].values > 0.5
    e[df['TienePelotaEquipo'].values == 1] = 1
    e[(df['Pase'].values == 1) & tiene] = 2
    e[(df['Disparo'].values == 1) & tiene] = 3
    return e


# --------------------------------------- 6. Multi-modelo (mixture of experts)
EXPERTOS = ['Recover', 'Approach', 'Pass', 'Shoot']


def enrutar(df, dist_disparo=15.0, dist_pase=10.0):
    """
    Enrutado de inferencia, replicado de AIController.cs. NO usa las etiquetas:
    en ejecucion Unity no sabe si el jugador va a pasar, solo ve la geometria.

    Es una diferencia importante respecto al entrenamiento original, que
    particionaba los datos POR ETIQUETA (Pase == 1 iba al experto Pass). Un
    experto entrenado sobre una particion que en ejecucion nunca se puede
    reproducir es un experto inalcanzable.
    """
    r = np.zeros(len(df), dtype=int)                       # 0 = Recover
    equipo = df['TienePelotaEquipo'].values
    mio = df['TienePelota'].values > 0.5
    d_gol = df['DistPorteriaContraria'].values
    d_ali = df['DistAliadoCercano'].values

    r[equipo == 1] = 1                                     # Approach
    con_balon = mio & (equipo == 1)
    r[con_balon & (d_ali < dist_pase)] = 2                 # Pass
    r[con_balon & (d_gol < dist_disparo)] = 3              # Shoot
    return r


CATALOGO = {
    'FNN dual-head':      dict(clase=FNNDualHead,     tipo='frame',    entrada=40),
    'Multi-modelo (MoE)': dict(clase=FNNDualHead,     tipo='moe',      entrada=40),
    'RNN (Elman)':        dict(clase=SoccerRNN,       tipo='secuencia', entrada=40, seq=2),
    'GRU':                dict(clase=SoccerGRU,       tipo='secuencia', entrada=40, seq=10),
    'FNN sliding window': dict(clase=FNNSlidingWindow, tipo='ventana',  entrada=80),
    'Inspirado en FSM':   dict(clase=SoccerFSM,       tipo='fsm',      entrada=40),
}
