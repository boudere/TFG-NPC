"""
Training Server — FastAPI
=========================
Servidor local que recibe datos CSV desde Unity, ejecuta el entrenamiento
del modelo FNN de clasificación, y devuelve el ONNX + scaler + métricas.

Uso:
    cd PythonTraining
    python training_server.py

Endpoint:
    POST /train
    Body JSON: { "csv_data": "...", "model_name": "MiModelo" }
    Respuesta: { "onnx_base64": "...", "scaler": {...}, "metrics": {...} }
"""
import sys
import os
import io
import json
import base64
import tempfile
import datetime

# Asegurar encoding UTF-8
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
import uvicorn

# Añadir PythonTrainingLegacy al path para importar train_clasificacion_fnn
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
LEGACY_DIR = os.path.join(PROJECT_ROOT, 'PythonTrainingLegacy')
sys.path.insert(0, LEGACY_DIR)

# Directorio donde Unity guarda sus Assets
ASSETS_DIR = os.path.join(PROJECT_ROOT, 'Assets')


# ============================================================================
# FastAPI App
# ============================================================================
app = FastAPI(
    title="TFG-NPC Training Server",
    description="Servidor para entrenar modelos FNN desde Unity",
    version="1.0.0"
)

# CORS: permitir peticiones desde cualquier origen (Unity usa localhost)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


# ============================================================================
# Modelos de request/response
# ============================================================================
class TrainRequest(BaseModel):
    csv_data: str       # Contenido completo del CSV (header + filas)
    model_name: str     # Nombre descriptivo del modelo (ej: "AgresivoDef_v3")


class TrainResponse(BaseModel):
    success: bool
    model_name: str
    onnx_base64: str            # Archivo ONNX codificado en Base64
    scaler_json: str            # Contenido del scaler.json como string JSON raw
    # Métricas explícitas (JsonUtility de Unity no soporta dict)
    acc_mov: float = 0.0
    prec_mov: float = 0.0
    rec_mov: float = 0.0
    f1_mov: float = 0.0
    acc_shoot: float = 0.0
    prec_shoot: float = 0.0
    rec_shoot: float = 0.0
    f1_shoot: float = 0.0
    acc_pass: float = 0.0
    prec_pass: float = 0.0
    rec_pass: float = 0.0
    f1_pass: float = 0.0
    # RoboK y RoboL: antes se calculaban en el entrenamiento pero no llegaban
    # a Unity, asi que el juego no podia mostrar como de bien roba el modelo.
    acc_robok: float = 0.0
    f1_robok: float = 0.0
    acc_robol: float = 0.0
    f1_robol: float = 0.0
    loss_final: float = 0.0
    message: str


# ============================================================================
# Endpoint de entrenamiento
# ============================================================================
@app.post("/train", response_model=TrainResponse)
async def train_model(request: TrainRequest):
    """
    Recibe datos CSV + nombre del modelo, entrena el FNN, y devuelve
    el ONNX + scaler + métricas.
    """
    model_name = request.model_name.strip()
    if not model_name:
        raise HTTPException(status_code=400, detail="model_name no puede estar vacío")

    # Validar que hay datos
    lines = request.csv_data.strip().split('\n')
    if len(lines) < 2:
        raise HTTPException(
            status_code=400,
            detail=f"CSV insuficiente: solo {len(lines)} líneas (necesita header + datos)"
        )

    print(f"\n{'='*60}")
    print(f"  SOLICITUD DE ENTRENAMIENTO: {model_name}")
    print(f"  CSV: {len(lines)-1} filas de datos")
    print(f"  Hora: {datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"{'='*60}")

    # Guardar CSV en un archivo temporal
    csv_tmp = None
    try:
        csv_tmp = tempfile.NamedTemporaryFile(
            mode='w', suffix='.csv', delete=False, encoding='utf-8',
            dir=ASSETS_DIR, prefix=f'_training_tmp_{model_name}_'
        )
        csv_tmp.write(request.csv_data)
        csv_tmp.close()
        csv_path = csv_tmp.name
        print(f"  CSV temporal guardado en: {csv_path}")

        # Definir rutas de salida
        onnx_output = os.path.join(ASSETS_DIR, f'SoccerModel_{model_name}.onnx')
        scaler_output = os.path.join(ASSETS_DIR, f'scaler_{model_name}.json')

        # Importar y ejecutar el entrenamiento
        import train_clasificacion_fnn as trainer

        ts = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
        result = trainer.train(
            timestamp=ts,
            csv_files=[csv_path],
            onnx_output_path=onnx_output,
            scaler_output=scaler_output
        )

        # Leer el ONNX generado y codificarlo en Base64
        if not os.path.exists(onnx_output):
            raise HTTPException(
                status_code=500,
                detail=f"El entrenamiento no generó el archivo ONNX en {onnx_output}"
            )

        with open(onnx_output, 'rb') as f:
            onnx_bytes = f.read()
        onnx_base64 = base64.b64encode(onnx_bytes).decode('utf-8')

        # Leer el scaler como string raw
        with open(scaler_output, 'r') as f:
            scaler_json_str = f.read()

        # Extraer métricas
        metrics = result.get('metrics', {})

        print(f"\n  ✓ Entrenamiento completado exitosamente")
        print(f"  ONNX: {len(onnx_bytes)} bytes")
        print(f"  Métricas: {metrics}")

        return TrainResponse(
            success=True,
            model_name=model_name,
            onnx_base64=onnx_base64,
            scaler_json=scaler_json_str,
            acc_mov=round(metrics.get('acc_mov', 0) * 100, 2),
            prec_mov=round(metrics.get('prec_mov', 0) * 100, 2),
            rec_mov=round(metrics.get('rec_mov', 0) * 100, 2),
            f1_mov=round(metrics.get('f1_mov', 0) * 100, 2),
            acc_shoot=round(metrics.get('acc_shoot', 0) * 100, 2),
            prec_shoot=round(metrics.get('prec_shoot', 0) * 100, 2),
            rec_shoot=round(metrics.get('rec_shoot', 0) * 100, 2),
            f1_shoot=round(metrics.get('f1_shoot', 0) * 100, 2),
            acc_pass=round(metrics.get('acc_pass', 0) * 100, 2),
            prec_pass=round(metrics.get('prec_pass', 0) * 100, 2),
            rec_pass=round(metrics.get('rec_pass', 0) * 100, 2),
            f1_pass=round(metrics.get('f1_pass', 0) * 100, 2),
            acc_robok=round(metrics.get('acc_robok', 0) * 100, 2),
            f1_robok=round(metrics.get('f1_robok', 0) * 100, 2),
            acc_robol=round(metrics.get('acc_robol', 0) * 100, 2),
            f1_robol=round(metrics.get('f1_robol', 0) * 100, 2),
            loss_final=round(metrics.get('loss_final', 0), 4),
            message=f"Modelo '{model_name}' entrenado exitosamente"
        )

    except HTTPException:
        raise
    except Exception as e:
        import traceback
        error_msg = f"Error durante el entrenamiento: {str(e)}"
        print(f"\n  ✗ {error_msg}")
        traceback.print_exc()
        raise HTTPException(status_code=500, detail=error_msg)

    finally:
        # Limpiar el CSV temporal
        if csv_tmp and os.path.exists(csv_tmp.name):
            try:
                os.unlink(csv_tmp.name)
                print(f"  CSV temporal eliminado: {csv_tmp.name}")
            except OSError:
                pass


# ============================================================================
# Health check
# ============================================================================
@app.get("/health")
async def health_check():
    return {"status": "ok", "message": "Training server is running"}


# ============================================================================
# Punto de entrada
# ============================================================================
if __name__ == "__main__":
    print("=" * 60)
    print("  TFG-NPC Training Server")
    print(f"  Assets dir: {ASSETS_DIR}")
    print(f"  Legacy dir: {LEGACY_DIR}")
    print("=" * 60)
    print("\nIniciando servidor en http://localhost:8000 ...")
    print("Endpoints disponibles:")
    print("  POST /train    — Entrena un modelo con datos CSV")
    print("  GET  /health   — Comprueba que el servidor está vivo")
    print("  GET  /docs     — Documentación interactiva (Swagger UI)")
    print()

    uvicorn.run(app, host="0.0.0.0", port=8000, log_level="info")
