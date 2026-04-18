# 📋 CHANGELOG — TFG-NPC: Implementación de IA con Aprendizaje Supervisado

Historial de cambios y decisiones de diseño del sistema de inteligencia artificial mediante aprendizaje supervisado para el proyecto de fútbol.

---

## [v0.4] — 2026-03-31

### 🐛 Bugs corregidos
- **`DistClosestAlly` siempre a 0**: El bucle de búsqueda de aliados en `Recorder.cs` incluía al propio jugador como primer aliado. El filtro `if (p == myPlayer)` no funcionaba porque comparaba la referencia del componente en lugar del `GameObject`. Corregido usando `if (p.gameObject == myPlayer.gameObject)`.
- **`Ally1Pos == MyPos`**: Consecuencia del bug anterior — `Ally1` era siempre el propio jugador. Corregido con el fix de arriba.

### 🔍 Análisis del dataset
- Ejecutado análisis estadístico del CSV `SoccerData_2026_03_31_18_22_05.csv` (1.666 filas × 45 columnas).
- Problemas encontrados:
  - `DistClosestAlly`: siempre 0 → bug corregido (ver arriba).
  - `ScoreTeam1/2`: siempre 0 → no conectados al `Arbitro`, feature pendiente.
  - Desequilibrio de labels: ~60% de filas con Input (0,0) → mitigado por el oversampling en `train.py`.
- El resto de columnas (porterías, pelota, posiciones, orientaciones) funcionan correctamente.

---

## [v0.3] — 2026-03-28

### ✨ Nuevas features en el dataset (28 columnas nuevas)
Ampliado `Recorder.cs` de **12 features** a **40 features**. Nuevas columnas añadidas:

| Columna(s) | Descripción |
|---|---|
| `MyFacingX`, `MyFacingZ` | Dirección hacia la que mira el jugador controlado (`transform.forward`) |
| `MyHasBall` | Booleano: ¿tiene la pelota este jugador concreto? |
| `DistToOwnGoal` | Distancia del jugador a su portería propia |
| `DistBallToOwnGoal` | Distancia de la pelota a la portería propia |
| `Ally1-3 PosX/Z` | Posición (X, Z) de los 3 aliados más cercanos |
| `Ally1-3 DirX/Z` | Dirección de movimiento (normalizada) de los 3 aliados más cercanos |
| `Enemy1-3 PosX/Z` | Posición (X, Z) de los 3 enemigos más cercanos |
| `Enemy1-3 DirX/Z` | Dirección de movimiento (normalizada) de los 3 enemigos más cercanos |

- Actualizado `train.py`: `feature_cols` lista las 40 columnas, `input_size = 40`.
- La dirección de movimiento se extrae del `linearVelocity` del `Rigidbody` de cada jugador, normalizado.

### 🔧 Auto-detección de porterías
- Eliminados los campos manuales del Inspector para las porterías.
- `Recorder.cs` busca automáticamente los componentes `Porteria` en la escena al iniciarse.
- Usa `porteria.team % 2 == myPlayer.id % 2` para distinguir portería propia de rival.
- Imprime en la Console de Unity qué porterías encontró.

---

## [v0.2] — 2026-03-25

### 📊 Validación del modelo (Split 80/20)
- Añadido split 80/20 en `train.py` usando `train_test_split` de scikit-learn.
- El modelo **solo se entrena con el 80%** de los datos; el 20% restante se usa exclusivamente para validar.
- Añadidas métricas de clasificación sobre outputs discretizados (`threshold = 0.3` → clases `-1`, `0`, `+1`):

#### Resultados (dataset de ~1.200 filas):
| Eje | Accuracy | Precision (macro) | Recall (macro) |
|---|---|---|---|
| InputX (Horizontal) | **80%** | 80% | 80% |
| InputZ (Vertical) | **82%** | 84% | 82% |
| **Acción combinada** | **65.6%** | 67.9% | 65.6% |

- Instalado `scikit-learn` en el entorno Python del proyecto.
- `train.py` imprime un `classification_report` completo por eje al finalizar cada entrenamiento.

---

## [v0.1] — 2026-03-13 / 2026-03-14

### 🏗️ Setup inicial del pipeline completo

#### Unity — `Recorder.cs`
- Creado `Recorder.cs` adaptado de un proyecto anterior de tanques.
- Graba un snapshot cada `snapshotTime` segundos (por defecto 0.1 s).
- **Features grabadas (12):**

| Columna | Descripción |
|---|---|
| `MyPosX`, `MyPosZ` | Posición del jugador controlado |
| `BallPosX`, `BallPosZ` | Posición de la pelota |
| `DistToBall` | Distancia del jugador a la pelota |
| `HasBallTeam` | Equipo que tiene la pelota (0=libre, 1=aliado, 2=rival) |
| `DistToRivalGoal` | Distancia a la portería rival |
| `ScoreTeam1`, `ScoreTeam2` | Marcador del partido |
| `DistBallToMyGoal` | Distancia de la pelota a la portería propia |
| `DistClosestAlly` | Distancia al aliado más cercano |
| `DistClosestEnemy` | Distancia al enemigo más cercano |

- **Labels grabados (2 + 2):** `InputX`, `InputZ`, `ActionShoot`, `ActionPass`.
- Usa `System.Globalization.CultureInfo.InvariantCulture` para que los decimales sean siempre `.` (evita el bug de Windows en español con `,`).
- Guarda el CSV automáticamente al cerrar el juego (`OnApplicationQuit`) y bajo demanda con `SaveToFile()`.

#### Python — `train.py`
- Red neuronal MLP simple: `12 → 64 → 64 → 2` con activaciones `ReLU` + `Tanh` final.
- Pérdida: `MSELoss` (regresión sobre valores continuos de joystick).
- Optimizador: `Adam`, lr = 0.001, 100 épocas.
- **Balanceo de clases por oversampling**: las acciones minoritarias se sobremuestrean hasta igualar la más frecuente.
- **Normalización de features**: se calculan `mean` y `std` del dataset y se guardan en `scaler.json` (para usarlos luego en Unity).
- Exporta el modelo a `.onnx` (opset 14) con fallback automático a `.pt` (TorchScript JIT).

#### Unity — `AIController.cs`
- Creado `AIController.cs` usando **Unity Sentis** (`com.unity.sentis`).
- Carga el archivo `.onnx` y ejecuta inferencia en cada `Update()`.
- Recoge las mismas 12 features que el `Recorder` y las pasa al modelo.
- Aplica los resultados (`aiInputX`, `aiInputZ`) directamente al `Rigidbody` del personaje.
- Deshabilita `CharacterGV` cuando la IA toma el control.

---

## 🗺️ Roadmap (pendiente)

- [ ] Conectar `ScoreTeam1/2` al marcador real del `Arbitro`.
- [ ] Instalar Unity Sentis (`com.unity.sentis`) y probar inferencia en tiempo real.
- [ ] Generar un nuevo CSV con el bug de `DistClosestAlly` corregido y reentrenar.
- [ ] Actualizar `AIController.cs` para usar las 40 features y aplicar la normalización del `scaler.json`.
- [ ] Grabar más datos con más variedad de situaciones (con pelota, sin pelota, cerca de portería...).
- [ ] Evaluar si mejorar la arquitectura de la red (más capas, LSTM para memoria temporal...).
