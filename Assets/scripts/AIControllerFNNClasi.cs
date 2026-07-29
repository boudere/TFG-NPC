using UnityEngine;
using System.Collections.Generic;
using System.IO;


/// <summary>
/// Controlador IA para el modelo FNN de Clasificación.
/// Usa el ONNX entrenado con PythonTrainingLegacy/train_clasificacion_fnn.py.
/// Salidas: movement_probs (9 clases) + action_probs (Shoot, Pass).
/// </summary>
public class AIControllerFNNClasi : MonoBehaviour
{
    [Header("AI Model (Clasificación FNN)")]
    [Tooltip("Modelo ONNX único exportado por train_clasificacion_fnn.py")]
    public Unity.InferenceEngine.ModelAsset onnxModelAsset;

    [Tooltip("El archivo scaler_clasificacion_fnn.json exportado en Python para normalizar los inputs")]
    public TextAsset scalerJson;

    [Header("Action Inference")]
    [Tooltip("Umbral para activar Disparo/Pase desde la salida sigmoide [0,1]")]
    [Range(0f, 1f)] public float actionThreshold = 0.35f;
    [Tooltip("Tiempo mínimo entre acciones para evitar spam")]
    public float actionCooldown = 1.0f;
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
    private Unity.InferenceEngine.Worker worker;
    private const int ExpectedInputSize = 40;
    private float nextActionTime = 0f;
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

        // Inicializar modelo
        if (onnxModelAsset != null)
        {
            var runtimeModel = Unity.InferenceEngine.ModelLoader.Load(onnxModelAsset);
            worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);
            Debug.Log("[AIControllerFNNClasi] Modelo de clasificación cargado OK.");
        }
        else
        {
            Debug.LogError("[AIControllerFNNClasi] ¡Falta asignar el modelo ONNX en el inspector!");
        }

        // Cargar scaler
        if (scalerJson != null)
        {
            try {
                JsonUtility.FromJsonOverwrite(scalerJson.text, scaler);
                Debug.Log("[AIControllerFNNClasi] Variables de normalización leídas desde scaler json.");
            } catch (System.Exception e) {
                Debug.LogWarning("[AIControllerFNNClasi] Fallo al parsear scaler JSON: " + e.Message);
            }
        }
        else
        {
            Debug.LogWarning("[AIControllerFNNClasi] Falta scaler JSON. Se usará input sin normalizar.");
        }
    }

    private void Update()
    {
        if (myPlayer == null || worker == null) return;

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

        // 5. Suavizado de Inputs (Mathf.Lerp)
        currentInputX = Mathf.Lerp(currentInputX, targetInputX, Time.deltaTime * movementSmoothing);
        currentInputZ = Mathf.Lerp(currentInputZ, targetInputZ, Time.deltaTime * movementSmoothing);

        // Procesar salida de acciones
        float shootProb = prediction.actions.Length > 0 ? prediction.actions[0] : 0f;
        float passProb  = prediction.actions.Length > 1 ? prediction.actions[1] : 0f;
        float roboKProb = prediction.actions.Length > 2 ? prediction.actions[2] : 0f;
        float roboLProb = prediction.actions.Length > 3 ? prediction.actions[3] : 0f;

        // 7. Aplicar movimiento
        Vector3 moveDir = new Vector3(currentInputX, 0f, currentInputZ).normalized;
        ApplyMovement(moveDir);

        // 8. Intentar acciones y obtener la acción realizada
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

        // 9. Desactivar CharacterGV
        if (characterGV != null) characterGV.enabled = false;

        // 10. Restricción de campo (seguridad: clamp sin frenar)
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
        using var inputTensor = new Unity.InferenceEngine.Tensor<float>(
            new Unity.InferenceEngine.TensorShape(1, inputFeatures.Length), inputFeatures);

        worker.Schedule(inputTensor);

        // movement_probs → [9 clases]
        using var movementTensor = worker.PeekOutput("movement_probs") as Unity.InferenceEngine.Tensor<float>;
        // action_probs → [Shoot, Pass, RoboK, RoboL]
        using var actionsTensor = worker.PeekOutput("action_probs") as Unity.InferenceEngine.Tensor<float>;

        if (movementTensor == null)
        {
            Debug.LogError("[AIControllerFNNClasi] No se pudo leer output 'movement_probs'.");
            return (new float[9], new float[4]);
        }

        float[] movement = movementTensor.DownloadToArray();
        float[] actions  = actionsTensor != null ? actionsTensor.DownloadToArray() : new float[4];

        return (movement, actions);
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
                nextActionTime = Time.time + actionCooldown;
                string actionStr = $"DISPARO (prob={shootProb:F2})";
                Debug.Log($"[AIControllerFNNClasi] ⚡ ACCIÓN EJECUTADA: {actionStr}");
                return actionStr;
            }

            if (doPass && Pase.instance != null)
            {
                Pase.instance.searchPlayersToPass("npc", transform.position, myPlayer.id);
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
                if (Bola.instance.EnPosesion && owner != null && WinTheBall.instance.EsPoseedorValidoParaRobar(owner))
                {
                    WinTheBall.instance.EjecutarRobo(gameObject);
                    nextActionTime = Time.time + actionCooldown;
                    string tipoRobo = doRoboK && doRoboL ? (roboKProb >= roboLProb ? "RoboK" : "RoboL") : (doRoboK ? "RoboK" : "RoboL");
                    float probMax = Mathf.Max(roboKProb, roboLProb);
                    string actionStr = $"ROBO [{tipoRobo}] (prob={probMax:F2})";
                    Debug.Log($"[AIControllerFNNClasi] ⚡ ACCIÓN EJECUTADA: {actionStr}");
                    return actionStr;
                }
            }
        }

        return "NINGUNA";
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
        int scoreT1 = 0;
        int scoreT2 = 0;
        int puntuacionPropia    = myPlayer.id % 2 == 0 ? scoreT1 : scoreT2;
        int puntuacionContraria = myPlayer.id % 2 == 0 ? scoreT2 : scoreT1;

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

    private void OnDestroy()
    {
        worker?.Dispose();
    }
}
