using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;


/// <summary>
/// Controlador IA para el modelo FNN de Clasificación.
/// Usa el ONNX entrenado con PythonTrainingLegacy/train_clasificacion_fnn.py.
/// Salidas: movement_probs (9 clases) + action_probs (Shoot, Pass).
///
/// Inferencia con ONNX Runtime (asus4/onnxruntime-unity) en vez de Sentis:
/// el .onnx se carga directamente desde disco (bytes/ruta), sin pasar por el
/// pipeline de assets de Unity. Esto permite recargar el modelo en caliente
/// (ReloadModel) tanto en el Editor como en una build ya compilada, algo que
/// Sentis no soporta fuera del Editor.
/// </summary>
public class AIControllerFNNClasi : MonoBehaviour
{
    [Header("AI Model (ONNX Runtime)")]
    [Tooltip("Nombre del archivo .onnx activo (el que descarga TrainingClient tras entrenar)")]
    public string activeModelFileName = "SoccerModel_Active.onnx";
    [Tooltip("Nombre del archivo scaler.json activo")]
    public string activeScalerFileName = "scaler_Active.json";

    [Tooltip("Modelo .onnx que se incluye en la build como valor por defecto (Assets/StreamingAssets)")]
    public string defaultModelFileName = "SoccerModel_ClasificacionFNN.onnx";
    [Tooltip("Scaler.json que se incluye en la build como valor por defecto (Assets/StreamingAssets)")]
    public string defaultScalerFileName = "scaler_clasificacion_fnn.json";

    [Header("Action Inference")]
    [Tooltip("Umbral para activar Disparo/Pase/RoboK/RoboL desde la salida sigmoide [0,1].\n" +
         "0.45 medido sobre 9.3 min de juego grabado: es donde el ritmo de acciones del\n" +
         "modelo mas se parece al del humano sin caer en el spam. Subirlo a 0.80 no acierta\n" +
         "mas, solo actua menos: el robo baja al 43% de tu ritmo y el disparo al 54%.")]
    [Range(0f, 1f)] public float actionThreshold = 0.45f;
    [Tooltip("Tiempo mínimo entre acciones para evitar spam")]
    public float actionCooldown = 1.0f;

    [Header("Robo con sprint (equivalente a la tecla K del jugador)")]
    [Tooltip("Velocidad durante el sprint de robo. La del jugador humano es 300.")]
    public float sprintSpeed = 300f;
    [Tooltip("Segundos maximos que puede durar un sprint de robo.")]
    public float sprintTimeout = 4f;
    [Tooltip("Radio para dar por llegado el sprint. Se amplia solo si en un paso de fisica se avanza mas que esto.")]
    public float sprintArrivalRadius = 15f;
    [Tooltip("Si nos alejamos del objetivo mas que esto respecto a lo mas cerca que llegamos, se aborta.")]
    public float sprintToleranciaPerdida = 40f;
    [Tooltip("Margen de carrera sobre la distancia inicial al objetivo.")]
    public float sprintMargenPersecucion = 1.25f;
    [Tooltip("Dispersión angular del disparo en grados (0 = tiro perfecto)")]
    [Range(0f, 30f)] public float shootSpreadDeg = 8f;

    [Header("Movement")]
    [Tooltip("Velocidad máxima del NPC")]
    public float moveSpeed = 150f;
    [Tooltip("Fuerza máxima de steering (controla la suavidad del giro)")]
    public float maxSteeringForce = 8f;
    [Tooltip("Velocidad de giro en grados/segundo")]
    public float turnSpeedDeg = 540f;
    [Tooltip("Velocidad de suavizado de input (Mathf.Lerp)")]
    public float movementSmoothing = 10f;
    [Tooltip("Cantidad de ruido de exploración (0 = sin ruido)")]
    [Range(0f, 0.5f)] public float explorationNoise = 0.0f;

    [Header("References")]
    public PlayerID myPlayer;
    public Rigidbody myRigidbody;
    public CharacterGV characterGV;

    [Header("Goals")]
    [Tooltip("Si se deja vacío, se buscan automáticamente por componente Porteria")]
    public Transform rivalGoalTransform;
    public Transform ownGoalTransform;

    [Header("Field Constraints")]
    [Tooltip("Evita que la IA salga del mapa")]
    public bool constrainToField = true;
    public float fieldLimitX = 600f;
    public float fieldLimitZ = 500f;
    [Tooltip("Distancia desde el borde donde empieza la fuerza de repulsión")]
    public float boundaryMargin = 30f;

    [Header("Debug")]
    [Tooltip("Activa/desactiva los logs de inferencia en consola")]
    public bool enableDebugLogs = false;
    [Tooltip("Activa/desactiva el log de las 40 variables de entrada (¡Spam alto!)")]
    public bool logInputFeatures = false;
    [Tooltip("Intervalo mínimo entre logs (segundos)")]
    public float logInterval = 1.0f;

    // ── Internals ──
    private InferenceSession session;
    private const string OnnxInputName = "vector_observation";
    private const int ExpectedInputSize = 40;
    private float nextActionTime = 0f;

    // Estado del sprint de robo. El NPC no usa CharacterGV (lo desactiva en
    // Update), asi que no puede reutilizar el sprint que hay alli: necesita el
    // suyo, y mientras dura tiene que anular el movimiento del modelo.
    private bool _sprintActivo;
    private Transform _sprintObjetivo;
    private PlayerID _sprintPoseedor;
    private Vector3 _sprintDireccion;
    private Vector3 _sprintOrigen;
    private float _sprintFin;
    private float _sprintDistanciaPermitida;
    private float _sprintMejorDistancia;
    private bool scalerWarningShown = false;
    private float nextLogTime = 0f;

    // Inputs suavizados
    private float currentInputX = 0f;
    private float currentInputZ = 0f;

    [System.Serializable]
    public class ScalerData
    {
        public float[] mean = new float[0];
        public float[] std  = new float[0];
    }
    private ScalerData scaler = new ScalerData();

    private void Start()
    {
        if (myPlayer    == null) myPlayer    = GetComponent<PlayerID>();
        if (aiRecorder  == null) aiRecorder  = GetComponent<AIRecorder>();

        // Indicador flotante que marca en pantalla cual de los once jugadores
        // lleva el modelo. Se anyade aqui a proposito: este componente solo se
        // habilita en el jugador del modelo, asi que el marcador aparece justo
        // sobre el que toca sin tener que arrastrar nada en la escena.
        if (mostrarIndicador && GetComponent<IndicadorModelo>() == null)
            gameObject.AddComponent<IndicadorModelo>();
        if (myRigidbody == null) myRigidbody = GetComponent<Rigidbody>();
        if (characterGV == null) characterGV = GetComponent<CharacterGV>();

        // Buscar porterías automáticamente
        if (rivalGoalTransform == null || ownGoalTransform == null)
        {
            Porteria[] porterias = FindObjectsByType<Porteria>(FindObjectsSortMode.None);
            int myTeam = myPlayer != null ? myPlayer.id % 2 : 0;

            foreach (Porteria p in porterias)
            {
                bool isOwnGoal = (p.team % 2 == myTeam);
                if (isOwnGoal && ownGoalTransform == null)
                    ownGoalTransform = p.transform;
                else if (!isOwnGoal && rivalGoalTransform == null)
                    rivalGoalTransform = p.transform;
            }
        }

        // Marcador real. Tiene que leerse EXACTAMENTE igual que en Recorder.cs:
        // si el dataset se graba con el marcador de verdad y aqui se enviaran
        // ceros, el modelo veria en partida una entrada distinta de la que
        // aprendio, que es el fallo silencioso mas caro de todos.
        if (ownGoalTransform != null)
            _porteriaPropia = ownGoalTransform.GetComponentInParent<Porteria>()
                              ?? ownGoalTransform.GetComponent<Porteria>();
        if (rivalGoalTransform != null)
            _porteriaRival = rivalGoalTransform.GetComponentInParent<Porteria>()
                             ?? rivalGoalTransform.GetComponent<Porteria>();

        // Orden de preferencia:
        //   1. El modelo que el jugador eligio en la lista (AsignarModelo).
        //   2. El ultimo entrenado (SoccerModel_Active.onnx en persistentDataPath).
        //   3. El que viene empaquetado con la build (StreamingAssets).
        //
        // El paso 1 tiene que estar AQUI y no fuera: cuando
        // CharacterManagerInField habilita este componente, su Start todavia no
        // se ha ejecutado, asi que correria despues y machacaria la eleccion
        // cargando Active. Guardando la eleccion como "pendiente" da igual el
        // orden en que Unity llame a los Start.
        string onnxPath;
        string scalerPath;

        if (!string.IsNullOrEmpty(_onnxPendiente) && File.Exists(_onnxPendiente))
        {
            onnxPath = _onnxPendiente;
            scalerPath = _scalerPendiente;
            Debug.Log("[AIControllerFNNClasi] Usando el modelo elegido: " + onnxPath);
            ReloadModel(onnxPath, scalerPath);
            return;
        }

        if (!string.IsNullOrEmpty(_onnxPendiente))
        {
            Debug.LogWarning("[AIControllerFNNClasi] El modelo elegido ya no existe (" +
                             _onnxPendiente + "). Se usa el ultimo entrenado.");
        }

        onnxPath = Path.Combine(Application.persistentDataPath, activeModelFileName);
        scalerPath = Path.Combine(Application.persistentDataPath, activeScalerFileName);

        if (!File.Exists(onnxPath))
        {
            onnxPath = Path.Combine(Application.streamingAssetsPath, defaultModelFileName);
            scalerPath = Path.Combine(Application.streamingAssetsPath, defaultScalerFileName);
        }

        if (File.Exists(onnxPath))
        {
            ReloadModel(onnxPath, scalerPath);
        }
        else
        {
            Debug.LogError($"[AIControllerFNNClasi] No se encontró ningún modelo ONNX. Buscado en:\n{onnxPath}");
        }
    }

    /// <summary>
    /// Recarga el modelo y el scaler en caliente (Editor o build, sin reiniciar la escena).
    /// TrainingClient llama a este método en todos los AIControllerFNNClasi activos
    /// justo después de descargar un modelo recién entrenado.
    /// </summary>
    private string _onnxPendiente;
    private string _scalerPendiente;

    /// <summary>
    /// Fija el modelo que debe usar este NPC. Puede llamarse ANTES de que corra
    /// su Start (por ejemplo justo al habilitar el componente): la eleccion se
    /// guarda y Start la respeta. Si ya estaba arrancado, recarga en caliente.
    /// </summary>
    public void AsignarModelo(string onnxPath, string scalerPath)
    {
        _onnxPendiente = onnxPath;
        _scalerPendiente = scalerPath;

        // session != null significa que Start ya paso: hay que recargar ahora.
        if (session != null && !string.IsNullOrEmpty(onnxPath) && File.Exists(onnxPath))
            ReloadModel(onnxPath, scalerPath);
    }

    public void ReloadModel(string onnxPath, string scalerPath)
    {
        // Red de seguridad: si el ONNX Runtime que hay en el proceso no es el
        // que trae el juego, crear la sesion mata el proceso entero con una
        // violacion de acceso, que NO es una excepcion capturable. Mejor un NPC
        // quieto y un error claro en el log que un cierre en seco.
        if (!OnnxRuntimePreload.Verificado)
        {
            Debug.LogError("[AIControllerFNNClasi] ONNX Runtime no esta en condiciones " +
                           "(mira las lineas de [OnnxRuntimePreload] mas arriba). " +
                           "No se carga el modelo para no cerrar el juego.");
            return;
        }

        InferenceSession newSession;
        try
        {
            byte[] modelBytes = File.ReadAllBytes(onnxPath);
            var options = new SessionOptions();
            newSession = new InferenceSession(modelBytes, options);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AIControllerFNNClasi] Error cargando el modelo ONNX ({onnxPath}): {e.Message}");
            return;
        }

        var newScaler = new ScalerData();
        if (File.Exists(scalerPath))
        {
            try
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(scalerPath), newScaler);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AIControllerFNNClasi] Fallo al parsear scaler JSON ({scalerPath}): {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"[AIControllerFNNClasi] Falta scaler JSON en {scalerPath}. Se usará input sin normalizar.");
        }

        // Sustituir de forma segura: primero preparar lo nuevo, luego liberar lo viejo.
        session?.Dispose();
        session = newSession;
        scaler = newScaler;
        scalerWarningShown = false;

        Debug.Log($"[AIControllerFNNClasi] Modelo recargado desde {onnxPath}");
    }

    // ── FRECUENCIA DE INFERENCIA ───────────────────────────────────
    // Recorder.cs graba las partidas a 10 Hz (snapshotTime = 0.1 s). Si el modelo
    // se ejecutase en cada fotograma renderizado (~60 Hz) trabajaria en condiciones
    // distintas a las del entrenamiento: la ventana temporal, el estado oculto y la
    // diferencia entre fotogramas consecutivos dejarian de corresponderse con lo
    // aprendido. Este temporizador mantiene la inferencia a la misma frecuencia a la
    // que se grabaron los datos.
    [Header("Frecuencia de inferencia")]
    [Tooltip("Segundos entre inferencias. 0.1 = 10 Hz, la misma frecuencia a la que Recorder.cs graba las partidas.")]
    public float inferenceInterval = 0.1f;

    private float _acumuladorInferencia = 0f;
    private bool  _hayDecision = false;
    private float _decInputX = 0f;
    private float _decInputZ = 0f;

    private void Update()
    {
        if (myPlayer == null || session == null) return;

        // Congelacion compartida: mientras el jugador este en un reset (el saque
        // de 4 segundos tras un gol), la IA se queda quieta como todos los
        // demas. Antes era la unica que seguia moviendose porque resetByTag
        // congela el rol y el CharacterGV, pero no a este componente.
        if (EnResetCompartido())
        {
            if (_sprintActivo) TerminarSprintRobo(false, "reset del partido");
            if (myRigidbody != null)
                myRigidbody.linearVelocity = new Vector3(0f, myRigidbody.linearVelocity.y, 0f);
            if (characterGV != null) characterGV.enabled = false;
            return;
        }

        // Mientras dura el sprint de robo el modelo no conduce: el NPC va a por
        // el poseedor igual que tu con la K. Se salta tambien la inferencia,
        // que en esos segundos no aporta nada y cuesta CPU.
        if (_sprintActivo)
        {
            ActualizarSprintRobo();
            if (characterGV != null) characterGV.enabled = false;
            return;
        }

        // Inferencia a frecuencia fija (ver comentario de inferenceInterval).
        _acumuladorInferencia += Time.deltaTime;
        if (_acumuladorInferencia >= inferenceInterval || !_hayDecision)
        {
            _acumuladorInferencia -= inferenceInterval;
            if (_acumuladorInferencia < 0f || _acumuladorInferencia > inferenceInterval)
                _acumuladorInferencia = 0f;
            _hayDecision = true;
            Decidir();
        }

        // Suavizado y actuacion: cada fotograma, hacia la ultima decision tomada.
        currentInputX = Mathf.Lerp(currentInputX, _decInputX, Time.deltaTime * movementSmoothing);
        currentInputZ = Mathf.Lerp(currentInputZ, _decInputZ, Time.deltaTime * movementSmoothing);

        Vector3 moveDir = new Vector3(currentInputX, 0f, currentInputZ).normalized;
        ApplyMovement(moveDir);

        if (characterGV != null) characterGV.enabled = false;

        if (constrainToField)
        {
            Vector3 pos = transform.position;
            if (Mathf.Abs(pos.x) > fieldLimitX || Mathf.Abs(pos.z) > fieldLimitZ)
            {
                pos.x = Mathf.Clamp(pos.x, -fieldLimitX, fieldLimitX);
                pos.z = Mathf.Clamp(pos.z, -fieldLimitZ, fieldLimitZ);
                transform.position = pos;
            }
        }
    }

    /// <summary>
    /// Una decision del modelo. Se ejecuta a inferenceInterval, no cada fotograma.
    /// </summary>
    private void Decidir()
    {
        // 1. Recopilar las 40 features
        float[] inputs = RecopilarVariablesDelEntorno();
        if (inputs.Length != ExpectedInputSize)
        {
            Debug.LogError($"[AIControllerFNNClasi] Features inválidas: {inputs.Length}. Esperado: {ExpectedInputSize}.");
            return;
        }

        // 2. Normalizar
        float[] normalizedInputs = (float[])inputs.Clone();
        NormalizeFeatures(normalizedInputs);

        // Debug de features de entrada
        if (logInputFeatures && enableDebugLogs && Time.time >= nextLogTime)
        {
            string featuresLog = "[FNNClasi Features] ";
            for (int i = 0; i < normalizedInputs.Length; i++)
            {
                featuresLog += $"{normalizedInputs[i]:F2}, ";
            }
            Debug.Log(featuresLog);
        }

        // 3. Ejecutar inferencia
        var prediction = Predecir(normalizedInputs);

        // Procesar salida de movimiento (9 clases)
        int bestClass = 4; // 4 = Quieto por defecto
        float maxProb = -1f;
        if (prediction.movement.Length == 9)
        {
            for (int i = 0; i < 9; i++)
            {
                if (prediction.movement[i] > maxProb)
                {
                    maxProb = prediction.movement[i];
                    bestClass = i;
                }
            }
        }

        // Mapeo inverso de clase a InputX, InputZ (-1, 0, 1)
        // class = (X + 1) * 3 + (Z + 1)
        float targetInputX = (bestClass / 3) - 1f;
        float targetInputZ = (bestClass % 3) - 1f;

        // 4. Ruido de exploración (opcional) sobre el target
        if (explorationNoise > 0f)
        {
            targetInputX = Mathf.Clamp(targetInputX + Random.Range(-explorationNoise, explorationNoise), -1f, 1f);
            targetInputZ = Mathf.Clamp(targetInputZ + Random.Range(-explorationNoise, explorationNoise), -1f, 1f);
        }

        // 5. Guardar la decision; el suavizado se aplica en Update, cada fotograma.
        _decInputX = targetInputX;
        _decInputZ = targetInputZ;

        // Procesar salida de acciones
        float shootProb = prediction.actions.Length > 0 ? prediction.actions[0] : 0f;
        float passProb  = prediction.actions.Length > 1 ? prediction.actions[1] : 0f;
        float roboKProb = prediction.actions.Length > 2 ? prediction.actions[2] : 0f;
        float roboLProb = prediction.actions.Length > 3 ? prediction.actions[3] : 0f;

        // 7. Intentar acciones y obtener la accion realizada
        string actionExecuted = TryApplyAction(shootProb, passProb, roboKProb, roboLProb);

        // 6. Log periódico con diagnóstico completo
        if (enableDebugLogs && Time.time >= nextLogTime)
        {
            nextLogTime = Time.time + logInterval;
            
            // Construir string con las 9 probabilidades de movimiento
            string probsLog = "";
            if (prediction.movement.Length == 9)
            {
                for (int i = 0; i < 9; i++)
                {
                    if (i == bestClass) probsLog += $"[C{i}:{prediction.movement[i]:F2}] ";
                    else probsLog += $"C{i}:{prediction.movement[i]:F2} ";
                }
            }

            bool hasBall = HasBallControl();
            string ballStatus = hasBall ? "CON PELOTA" : "sin pelota";
            string shootStatus = shootProb >= actionThreshold ? "ACTIVAR" : $"bajo ({shootProb:F3})";
            string passStatus  = passProb  >= actionThreshold ? "ACTIVAR" : $"bajo ({passProb:F3})";
            string roboKStatus = roboKProb >= actionThreshold ? "ACTIVAR" : $"bajo ({roboKProb:F3})";
            string roboLStatus = roboLProb >= actionThreshold ? "ACTIVAR" : $"bajo ({roboLProb:F3})";
            
            Debug.Log($"[FNNClasi] ClaseMov={bestClass} ({maxProb*100:F0}%) | DirLerp=({currentInputX:F2},{currentInputZ:F2})\n" +
                      $"           Probs: {probsLog}\n" +
                      $"           {ballStatus} | Shoot={shootProb:F3} [{shootStatus}] | Pass={passProb:F3} [{passStatus}] | RoboK={roboKProb:F3} [{roboKStatus}] | RoboL={roboLProb:F3} [{roboLStatus}]\n" +
                      $"           ⚡ ACCIÓN REALIZADA: {actionExecuted}");
        }

    }

    // ── MOVIMIENTO — Seek Steering Behavior (Reynolds) + Boundary Avoidance ──
    private void ApplyMovement(Vector3 moveDir)
    {
        if (moveDir.sqrMagnitude < 0.001f)
        {
            Vector3 brakeVel = myRigidbody.linearVelocity;
            brakeVel.x *= 0.9f;
            brakeVel.z *= 0.9f;
            myRigidbody.linearVelocity = brakeVel;
            return;
        }

        // 1. Velocidad deseada
        Vector3 desiredVelocity = moveDir.normalized * moveSpeed;

        // 2. Boundary Avoidance — fuerza de repulsión suave cerca de los bordes
        if (constrainToField && boundaryMargin > 0f)
        {
            Vector3 pos = transform.position;
            Vector3 boundaryForce = Vector3.zero;

            float distToEdgeXPos = fieldLimitX - pos.x;
            float distToEdgeXNeg = fieldLimitX + pos.x;
            if (distToEdgeXPos < boundaryMargin)
                boundaryForce.x -= (1f - distToEdgeXPos / boundaryMargin);
            if (distToEdgeXNeg < boundaryMargin)
                boundaryForce.x += (1f - distToEdgeXNeg / boundaryMargin);

            float distToEdgeZPos = fieldLimitZ - pos.z;
            float distToEdgeZNeg = fieldLimitZ + pos.z;
            if (distToEdgeZPos < boundaryMargin)
                boundaryForce.z -= (1f - distToEdgeZPos / boundaryMargin);
            if (distToEdgeZNeg < boundaryMargin)
                boundaryForce.z += (1f - distToEdgeZNeg / boundaryMargin);

            desiredVelocity += boundaryForce * moveSpeed;
            if (desiredVelocity.magnitude > moveSpeed)
                desiredVelocity = desiredVelocity.normalized * moveSpeed;
        }

        // 3. Velocidad actual (plano XZ)
        Vector3 currentVelocity = myRigidbody.linearVelocity;
        currentVelocity.y = 0f;

        // 4. Fuerza de steering = deseada - actual
        Vector3 steering = desiredVelocity - currentVelocity;
        if (steering.magnitude > maxSteeringForce)
            steering = steering.normalized * maxSteeringForce;

        // 5. Aplicar
        Vector3 newVelocity = currentVelocity + steering;
        if (newVelocity.magnitude > moveSpeed)
            newVelocity = newVelocity.normalized * moveSpeed;

        myRigidbody.linearVelocity = new Vector3(newVelocity.x, myRigidbody.linearVelocity.y, newVelocity.z);

        // Rotación suave
        if (newVelocity.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(newVelocity.normalized, Vector3.up);
            myRigidbody.MoveRotation(
                Quaternion.RotateTowards(myRigidbody.rotation, targetRot, turnSpeedDeg * Time.deltaTime)
            );
        }
    }

    // ── INFERENCIA ──
    private (float[] movement, float[] actions) Predecir(float[] inputFeatures)
    {
        var inputTensor = new DenseTensor<float>(inputFeatures, new[] { 1, inputFeatures.Length });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(OnnxInputName, inputTensor)
        };

        float[] movement = new float[9];
        float[] actions = new float[4];

        using (var results = session.Run(inputs))
        {
            foreach (var result in results)
            {
                if (result.Name == "movement_probs")
                    movement = result.AsEnumerable<float>().ToArray();
                else if (result.Name == "action_probs")
                    actions = result.AsEnumerable<float>().ToArray();
            }
        }

        if (movement.Length != 9)
            Debug.LogError("[AIControllerFNNClasi] No se pudo leer output 'movement_probs'.");

        return (movement, actions);
    }

    // Grabador de comportamiento. Puede ser null: solo esta habilitado en
    // el jugador que lleva el modelo, y solo si se quieren mapas de calor.
    private AIRecorder aiRecorder;

    // Contadores de goles, cacheados igual que en Recorder.cs.
    private Porteria _porteriaPropia;
    private Porteria _porteriaRival;

    [Header("Ayudas visuales")]
    [Tooltip("Muestra un marcador flotante sobre este jugador indicando que " +
             "lleva el modelo. En partida se puede ocultar con F2.")]
    public bool mostrarIndicador = true;

    /// <summary>Avisa al grabador de que se acaba de ejecutar una accion.</summary>
    private void AnotarAccion(int accion)
    {
        if (aiRecorder != null) aiRecorder.RegistrarAccion(accion);
    }

    // ── ACCIONES ──
    private string TryApplyAction(float shootProb, float passProb, float roboKProb, float roboLProb)
    {
        if (Time.time < nextActionTime) return "EN_COOLDOWN";

        if (HasBallControl())
        {
            bool doShoot = shootProb >= actionThreshold;
            bool doPass  = passProb  >= actionThreshold;

            if (doShoot && doPass)
                doShoot = shootProb >= passProb;

            if (doShoot && Shoot.instance != null)
            {
                AimAtGoal();
                Shoot.instance.disparoLibre();
                AnotarAccion(AIRecorder.ACCION_DISPARO);
                nextActionTime = Time.time + actionCooldown;
                string actionStr = $"DISPARO (prob={shootProb:F2})";
                Debug.Log($"[AIControllerFNNClasi] ⚡ ACCIÓN EJECUTADA: {actionStr}");
                return actionStr;
            }

            if (doPass && Pase.instance != null)
            {
                Pase.instance.searchPlayersToPass("npc", transform.position, myPlayer.id);
                AnotarAccion(AIRecorder.ACCION_PASE);
                nextActionTime = Time.time + actionCooldown;
                string actionStr = $"PASE (prob={passProb:F2})";
                Debug.Log($"[AIControllerFNNClasi] ⚡ ACCIÓN EJECUTADA: {actionStr}");
                return actionStr;
            }
        }
        else
        {
            bool doRoboK = roboKProb >= actionThreshold;
            bool doRoboL = roboLProb >= actionThreshold;

            if ((doRoboK || doRoboL) && WinTheBall.instance != null && Bola.instance != null)
            {
                PlayerID owner = Bola.instance.Owner;
                int miId = myPlayer != null ? myPlayer.id : -1;

                if (Bola.instance.EnPosesion && owner != null &&
                    WinTheBall.instance.EsPoseedorValidoParaRobar(owner, miId))
                {
                    float distPoseedor = Vector3.Distance(transform.position, owner.transform.position);
                    float distBalon = Vector3.Distance(transform.position, Bola.instance.transform.position);

                    // Cada tecla hace lo suyo, igual que para el jugador humano:
                    //   L -> robo directo, solo si ya esta pegado.
                    //   K -> si esta pegado roba; si no, esprinta y roba al llegar.
                    bool prefiereK = doRoboK && (!doRoboL || roboKProb >= roboLProb);

                    if (distBalon < WinTheBall.instance.distanciaMaxima)
                    {
                        WinTheBall.instance.EjecutarRobo(gameObject, miId);
                        AnotarAccion(prefiereK ? AIRecorder.ACCION_ROBO_K : AIRecorder.ACCION_ROBO_L);
                        nextActionTime = Time.time + actionCooldown;
                        string actionStr = $"ROBO [{(prefiereK ? "RoboK" : "RoboL")}] directo (prob={Mathf.Max(roboKProb, roboLProb):F2})";
                        Debug.Log($"[AIControllerFNNClasi] ⚡ ACCIÓN EJECUTADA: {actionStr}");
                        return actionStr;
                    }

                    if (prefiereK && distPoseedor <= WinTheBall.instance.rangoSprint)
                    {
                        IniciarSprintRobo(owner);
                        AnotarAccion(AIRecorder.ACCION_ROBO_K);
                        nextActionTime = Time.time + actionCooldown;
                        string actionStr = $"SPRINT ROBO [RoboK] hacia {owner.id} a {distPoseedor:F0} (prob={roboKProb:F2})";
                        Debug.Log($"[AIControllerFNNClasi] ⚡ ACCIÓN EJECUTADA: {actionStr}");
                        return actionStr;
                    }
                }
            }
        }

        return "NINGUNA";
    }

    // ======================================================================
    // SPRINT DE ROBO (equivalente a la tecla K del jugador humano)
    // ======================================================================
    /// <summary>
    /// Consulta TODOS los PlayerID de este GameObject (el rol y el CharacterGV
    /// conviven en el mismo objeto) y devuelve true si alguno esta en reset.
    /// Se miran todos a proposito: myPlayer puede haber resuelto a cualquiera
    /// de los dos, y basta con que uno diga que hay que estarse quieto.
    /// </summary>
    private bool EnResetCompartido()
    {
        PlayerID[] todos = GetComponents<PlayerID>();
        for (int i = 0; i < todos.Length; i++)
        {
            if (todos[i] != null && todos[i].EnReset) return true;
        }
        return false;
    }

    private void IniciarSprintRobo(PlayerID poseedor)
    {
        if (poseedor == null) return;

        Vector3 dir = poseedor.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude <= 0.001f) return;

        float distanciaInicial = dir.magnitude;

        _sprintObjetivo = poseedor.transform;
        _sprintPoseedor = poseedor;
        _sprintDireccion = dir / distanciaInicial;
        _sprintOrigen = transform.position;
        _sprintFin = Time.time + sprintTimeout;
        _sprintMejorDistancia = distanciaInicial;

        // La carrera permitida sale de lo lejos que esta el objetivo, no de un
        // numero fijo: asi no se corren 400 unidades para un objetivo a 150.
        _sprintDistanciaPermitida = distanciaInicial * sprintMargenPersecucion + 25f;
        _sprintActivo = true;
    }

    private void TerminarSprintRobo(bool intentarRobo, string motivo)
    {
        _sprintActivo = false;
        _sprintObjetivo = null;
        _sprintPoseedor = null;
        _sprintDireccion = Vector3.zero;

        if (myRigidbody != null)
            myRigidbody.linearVelocity = new Vector3(0f, myRigidbody.linearVelocity.y, 0f);

        if (!intentarRobo || WinTheBall.instance == null || Bola.instance == null)
        {
            if (enableDebugLogs) Debug.Log($"[AIControllerFNNClasi] Sprint de robo terminado: {motivo}");
            return;
        }

        int miId = myPlayer != null ? myPlayer.id : -1;
        float distBalon = Vector3.Distance(transform.position, Bola.instance.transform.position);
        float umbral = Mathf.Max(WinTheBall.instance.distanciaMaxima, sprintArrivalRadius + 5f);

        if (distBalon <= umbral)
        {
            WinTheBall.instance.EjecutarRobo(gameObject, miId);
            if (enableDebugLogs) Debug.Log($"[AIControllerFNNClasi] ⚡ ROBO al terminar el sprint ({motivo})");
        }
        else if (enableDebugLogs)
        {
            Debug.Log($"[AIControllerFNNClasi] Sprint terminado ({motivo}) pero el balon esta a {distBalon:F0}: no roba.");
        }
    }

    private void ActualizarSprintRobo()
    {
        if (Time.time >= _sprintFin)
        {
            TerminarSprintRobo(true, "timeout");
            return;
        }

        // Si el balon cambia de duenyo o se suelta, el motivo del sprint ya no existe.
        if (Bola.instance == null || !Bola.instance.EnPosesion ||
            Bola.instance.Owner != _sprintPoseedor || _sprintObjetivo == null)
        {
            TerminarSprintRobo(false, "el objetivo ha perdido el balon");
            return;
        }

        if (Vector3.Distance(transform.position, _sprintOrigen) >= _sprintDistanciaPermitida)
        {
            TerminarSprintRobo(true, "distancia maxima recorrida");
            return;
        }

        Vector3 hacia = _sprintObjetivo.position - transform.position;
        hacia.y = 0f;
        float distancia = hacia.magnitude;

        // Si nos alejamos en vez de acercarnos, corre mas que nosotros.
        if (distancia < _sprintMejorDistancia)
        {
            _sprintMejorDistancia = distancia;
        }
        else if (distancia > _sprintMejorDistancia + sprintToleranciaPerdida)
        {
            TerminarSprintRobo(true, "me alejo del objetivo");
            return;
        }

        // Radio de llegada proporcional al avance por paso de fisica: a 300 u/s
        // el rigidbody avanza ~6 unidades por FixedUpdate y un radio fijo
        // pequenyo se atravesaria de un salto sin detectarse.
        float radioLlegada = Mathf.Max(sprintArrivalRadius, sprintSpeed * Time.fixedDeltaTime * 1.5f);
        if (distancia <= radioLlegada)
        {
            TerminarSprintRobo(true, "llegada");
            return;
        }

        if (distancia > 0.01f) _sprintDireccion = hacia / distancia;

        Vector3 v = _sprintDireccion * sprintSpeed;
        myRigidbody.linearVelocity = new Vector3(v.x, myRigidbody.linearVelocity.y, v.z);

        if (v.sqrMagnitude > 0.01f)
        {
            Quaternion rot = Quaternion.LookRotation(_sprintDireccion, Vector3.up);
            myRigidbody.MoveRotation(
                Quaternion.RotateTowards(myRigidbody.rotation, rot, turnSpeedDeg * Time.deltaTime));
        }
    }

    private bool HasBallControl()
    {
        if (Bola.instance == null || !Bola.instance.EnPosesion || Bola.instance.Owner == null || myPlayer == null)
            return false;

        PlayerID ownerID = Bola.instance.Owner.GetComponent<PlayerID>();
        return ownerID != null && ownerID == myPlayer;
    }

    /// <summary>
    /// Aim Assist: rota el NPC hacia la portería rival con dispersión aleatoria
    /// para que el disparo vaya aproximadamente hacia la portería.
    /// </summary>
    private void AimAtGoal()
    {
        if (rivalGoalTransform == null) return;

        Vector3 dir = rivalGoalTransform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        // Añadir dispersión angular aleatoria
        float spread = Random.Range(-shootSpreadDeg, shootSpreadDeg);
        dir = Quaternion.Euler(0f, spread, 0f) * dir;

        transform.rotation = Quaternion.LookRotation(dir);
    }

    // ── NORMALIZACIÓN ──
    private void NormalizeFeatures(float[] inputs)
    {
        if (scaler != null && scaler.mean != null && scaler.std != null &&
            scaler.mean.Length == inputs.Length && scaler.std.Length == inputs.Length)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = (inputs[i] - scaler.mean[i]) / scaler.std[i];
                inputs[i] = Mathf.Clamp(inputs[i], -3f, 3f);
            }
        }
        else if (!scalerWarningShown)
        {
            Debug.LogWarning($"[AIControllerFNNClasi] scaler.json no coincide con {inputs.Length} features.");
            scalerWarningShown = true;
        }
    }

    /// <summary>
    /// Recopila las 40 features con posiciones RELATIVAS al jugador.
    /// </summary>
    private float[] RecopilarVariablesDelEntorno()
    {
        Vector3 myPos = transform.position;

        // ---- Porterías Relativas ----
        Vector3 rivalGoalPos = rivalGoalTransform != null ? rivalGoalTransform.position : Vector3.zero;
        Vector3 ownGoalPos   = ownGoalTransform != null ? ownGoalTransform.position : Vector3.zero;

        float relRivalGoalX = rivalGoalPos.x - myPos.x;
        float relRivalGoalZ = rivalGoalPos.z - myPos.z;
        float relOwnGoalX = ownGoalPos.x - myPos.x;
        float relOwnGoalZ = ownGoalPos.z - myPos.z;

        // ---- Pelota Relativa ----
        Vector3 ballPos  = Bola.instance != null ? Bola.instance.transform.position : Vector3.zero;
        float relBallX   = ballPos.x - myPos.x;
        float relBallZ   = ballPos.z - myPos.z;
        float distToBall = Vector3.Distance(myPos, ballPos);

        // ---- ¿Quién tiene la pelota? ----
        int hasBallTeam = 0;
        int myHasBall   = 0;
        if (Bola.instance != null && Bola.instance.EnPosesion && Bola.instance.Owner != null)
        {
            PlayerID ownerID = Bola.instance.Owner.GetComponent<PlayerID>();
            if (ownerID != null)
            {
                bool sameTeam = (ownerID.id % 2 == myPlayer.id % 2);
                hasBallTeam = sameTeam ? 1 : 2;
                myHasBall   = (ownerID == myPlayer) ? 1 : 0;
            }
        }

        // ---- Métricas ----
        float distToRivalGoal   = Vector3.Distance(myPos, rivalGoalPos);
        float distToOwnGoal     = Vector3.Distance(myPos, ownGoalPos);
        float distBallToOwnGoal = Vector3.Distance(ballPos, ownGoalPos);

        // ---- Puntuaciones ----
        // goalCounterTeam de una porteria = goles ENCAJADOS por su equipo, asi
        // que los mios estan en la porteria rival y viceversa.
        int puntuacionPropia    = _porteriaRival  != null ? _porteriaRival.goalCounterTeam  : 0;
        int puntuacionContraria = _porteriaPropia != null ? _porteriaPropia.goalCounterTeam : 0;

        // ---- Clasificar jugadores ----
        PlayerID[] allPlayers = FindObjectsByType<PlayerID>(FindObjectsSortMode.None);

        var allies  = new List<(float dist, PlayerID p)>();
        var enemies = new List<(float dist, PlayerID p)>();

        foreach (PlayerID p in allPlayers)
        {
            if (p.gameObject == myPlayer.gameObject) continue;

            float d = Vector3.Distance(myPos, p.transform.position);

            if (p.id % 2 == myPlayer.id % 2)
                allies.Add((d, p));
            else
                enemies.Add((d, p));
        }

        allies.Sort((a, b) => a.dist.CompareTo(b.dist));
        enemies.Sort((a, b) => a.dist.CompareTo(b.dist));

        float distClosestAlly  = allies.Count  > 0 ? allies[0].dist  : 999f;
        float distClosestEnemy = enemies.Count > 0 ? enemies[0].dist : 999f;

        // ---- Helper: datos RELATIVOS de un jugador ----
        (float relPx, float relPz, float dx, float dz) GetPlayerData(List<(float dist, PlayerID p)> list, int index)
        {
            if (index >= list.Count) return (0f, 0f, 0f, 0f);
            PlayerID p  = list[index].p;
            Vector3 pos = p.transform.position;
            Rigidbody rb = p.GetComponent<Rigidbody>();
            Vector3 vel  = rb != null ? rb.linearVelocity : Vector3.zero;
            Vector3 dir  = vel.magnitude > 0.01f ? vel.normalized : Vector3.zero;

            float rx = pos.x - myPos.x;
            float rz = pos.z - myPos.z;
            return (rx, rz, dir.x, dir.z);
        }

        var (a1px, a1pz, a1dx, a1dz) = GetPlayerData(allies, 0);
        var (a2px, a2pz, a2dx, a2dz) = GetPlayerData(allies, 1);
        var (a3px, a3pz, a3dx, a3dz) = GetPlayerData(allies, 2);

        var (e1px, e1pz, e1dx, e1dz) = GetPlayerData(enemies, 0);
        var (e2px, e2pz, e2dx, e2dz) = GetPlayerData(enemies, 1);
        var (e3px, e3pz, e3dx, e3dz) = GetPlayerData(enemies, 2);

        // 40 features
        float[] inputs = {
            relRivalGoalX, relRivalGoalZ,
            relOwnGoalX, relOwnGoalZ,
            myHasBall,
            relBallX, relBallZ,
            distToBall,
            hasBallTeam,
            distToRivalGoal,
            distToOwnGoal,
            puntuacionPropia, puntuacionContraria,
            distBallToOwnGoal,
            distClosestAlly,
            distClosestEnemy,
            a1px, a1pz, a1dx, a1dz,
            a2px, a2pz, a2dx, a2dz,
            a3px, a3pz, a3dx, a3dz,
            e1px, e1pz, e1dx, e1dz,
            e2px, e2pz, e2dx, e2dz,
            e3px, e3pz, e3dx, e3dz
        };

        return inputs;
    }

    private void OnDisable()
    {
        // Si nos desactivan a mitad de sprint, no dejar al rigidbody lanzado.
        if (_sprintActivo) TerminarSprintRobo(false, "componente desactivado");
    }

    private void OnDestroy()
    {
        session?.Dispose();
    }
}
