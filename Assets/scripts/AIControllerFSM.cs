using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Controlador IA basado en Máquina de Estados (FSM).
/// Un único modelo ONNX predice el estado (Defendiendo/Atacando/Pasando/Tirando)
/// y el movimiento (InputX, InputZ) simultáneamente.
/// La ejecución de cada estado reutiliza lógica adaptada de los scripts de roles
/// (Defensa.cs, Delantero.cs) y las acciones existentes (Pase.cs, Shoot.cs).
/// </summary>
public class AIControllerFSM : MonoBehaviour
{
    // ── Estados de la FSM ──
    public enum FSMState
    {
        Defendiendo = 0,
        Atacando    = 1,
        Pasando     = 2,
        Tirando     = 3
    }

    [Header("AI Model (FSM)")]
    [Tooltip("Modelo ONNX único exportado por train_fsm.py")]
    public Unity.InferenceEngine.ModelAsset modelFSM;

    [Tooltip("El archivo scaler_fsm.json exportado en Python para normalizar los inputs")]
    public TextAsset scalerJson;

    [Header("Action Cooldowns")]
    [Tooltip("Tiempo mínimo entre acciones de Pase/Tiro para evitar spam")]
    public float actionCooldown = 2f;

    [Header("State Hysteresis")]
    [Tooltip("Tiempo mínimo que el NPC permanece en un estado antes de poder cambiar")]
    public float minStateTime = 2.0f;

    [Header("Movement")]
    [Tooltip("Velocidad de movimiento del NPC")]
    public float moveSpeed = 100f;
    [Tooltip("Velocidad de giro en grados/segundo")]
    public float turnSpeedDeg = 540f;

    [Header("Defending Behavior")]
    [Tooltip("Posición entre la pelota y portería propia. 0=portería, 1=pelota (solo cuando aliado tiene pelota)")]
    [Range(0f, 1f)] public float defendInterceptLerp = 0.4f;

    [Header("Attacking Behavior")]
    [Tooltip("Distancia al target de ataque para elegir uno nuevo")]
    public float attackRetargetDistance = 50f;

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
    public float fieldLimitZ = 350f;

    [Header("Debug")]
    public FSMState currentState = FSMState.Defendiendo;
    [Tooltip("Probabilidades brutas del modelo por estado")]
    public float[] stateProbabilities = new float[4];
    [Tooltip("Activa/desactiva los logs de estado y acciones en consola")]
    public bool enableDebugLogs = true;
    [Tooltip("Intervalo mínimo entre logs (segundos) para no saturar la consola")]
    public float logInterval = 0.5f;

    // ── Internals ──
    private Unity.InferenceEngine.Worker worker;
    private const int ExpectedInputSize = 40;
    private const int NumStates = 4;
    private float nextActionTime = 0f;
    private bool scalerWarningShown = false;
    private FSMState previousState = FSMState.Defendiendo;
    private float nextLogTime = 0f;
    private string lastActionDetail = "";
    private float stateEnteredTime = 0f;  // Para hysteresis

    // Target para comportamiento de ataque (posición aleatoria en área rival)
    private Vector3 attackTarget = Vector3.zero;

    [System.Serializable]
    public class ScalerData
    {
        public float[] mean = new float[0];
        public float[] std  = new float[0];
    }
    private ScalerData scaler = new ScalerData();

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

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
        if (modelFSM != null)
        {
            var runtimeModel = Unity.InferenceEngine.ModelLoader.Load(modelFSM);
            worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);
            Debug.Log("[AIControllerFSM] Modelo FSM cargado OK.");
        }
        else
        {
            Debug.LogError("[AIControllerFSM] Falta el modelo ONNX FSM. Asígnalo en el inspector.");
        }

        // Cargar scaler
        if (scalerJson != null)
        {
            try {
                JsonUtility.FromJsonOverwrite(scalerJson.text, scaler);
                Debug.Log("[AIControllerFSM] Variables de normalización leídas desde scaler_fsm.json.");
            } catch (System.Exception e) {
                Debug.LogWarning("[AIControllerFSM] Fallo al parsear scaler JSON: " + e.Message);
            }
        }
        else
        {
            Debug.LogWarning("[AIControllerFSM] Falta scaler JSON. Se usará input sin normalizar.");
        }
    }

    private void Update()
    {
        if (myPlayer == null || worker == null) return;

        // 1. Recopilar observaciones del entorno (40 features)
        float[] rawInputs = RecopilarVariablesDelEntorno();
        if (rawInputs.Length != ExpectedInputSize)
        {
            Debug.LogError($"[AIControllerFSM] Features inválidas: {rawInputs.Length}. Esperado: {ExpectedInputSize}.");
            return;
        }

        // 2. Normalizar
        float[] normalizedInputs = (float[])rawInputs.Clone();
        NormalizeFeatures(normalizedInputs);

        // 3. Ejecutar inferencia
        var prediction = Predecir(normalizedInputs);
        float[] stateLogits = prediction.stateLogits;
        float[] movement    = prediction.movement;

        // 4. Convertir logits a probabilidades (softmax) y elegir estado (argmax)
        stateProbabilities = Softmax(stateLogits);
        int bestState = Argmax(stateProbabilities);
        FSMState proposedState = (FSMState)bestState;

        // Hysteresis: solo cambiar de estado si ha pasado el tiempo mínimo
        if (proposedState != currentState)
        {
            if (Time.time - stateEnteredTime >= minStateTime)
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[FSM {myPlayer.id}] Transición: {currentState} → {proposedState}  " +
                              $"(Prob: Def={stateProbabilities[0]:F2} Ata={stateProbabilities[1]:F2} " +
                              $"Pas={stateProbabilities[2]:F2} Tir={stateProbabilities[3]:F2})");
                }
                currentState = proposedState;
                previousState = currentState;
                stateEnteredTime = Time.time;
            }
            // Si no ha pasado el tiempo mínimo, se queda en el estado actual
        }

        // 5. Ejecutar comportamiento del estado
        lastActionDetail = "";
        ExecuteState(currentState, movement, rawInputs);

        // Log periódico de estado + acción
        if (enableDebugLogs && Time.time >= nextLogTime)
        {
            nextLogTime = Time.time + logInterval;
            string probStr = $"Def={stateProbabilities[0]:F2} Ata={stateProbabilities[1]:F2} " +
                             $"Pas={stateProbabilities[2]:F2} Tir={stateProbabilities[3]:F2}";
            Debug.Log($"[FSM {myPlayer.id}] Estado={currentState} | {lastActionDetail} | Prob: [{probStr}]");
        }

        // 6. Desactivar CharacterGV si existe (lo mismo que hace AIController)
        if (characterGV != null) characterGV.enabled = false;

        // 7. Restricción de campo
        if (constrainToField)
        {
            Vector3 pos = transform.position;
            if (Mathf.Abs(pos.x) > fieldLimitX || Mathf.Abs(pos.z) > fieldLimitZ)
            {
                pos.x = Mathf.Clamp(pos.x, -fieldLimitX, fieldLimitX);
                pos.z = Mathf.Clamp(pos.z, -fieldLimitZ, fieldLimitZ);
                transform.position = pos;
                myRigidbody.linearVelocity = Vector3.zero;
            }
        }
    }

    // ========================================================================
    // EJECUCIÓN DE ESTADOS
    // ========================================================================

    private void ExecuteState(FSMState state, float[] aiMovement, float[] rawInputs)
    {
         switch (state)
        {
            case FSMState.Defendiendo:
                ExecuteDefending(aiMovement, rawInputs);
                break;

            case FSMState.Atacando:
                ExecuteAttacking(aiMovement, rawInputs);
                break;

            case FSMState.Pasando:
                ExecutePassing(aiMovement);
                break;

            case FSMState.Tirando:
                ExecuteShooting(aiMovement);
                break;
        }
    }

    /// <summary>
    /// Estado DEFENDIENDO: Lógica adaptada de Defensa.cs
    /// Cuatro escenarios:
    ///  0. YO tengo la pelota → pasar a un compañero para sacar del área
    ///  1. Pelota LIBRE (nadie la tiene) → correr a recuperarla
    ///  2. RIVAL tiene la pelota → correr hacia la pelota para robarla
    ///  3. ALIADO tiene la pelota → posicionarse entre pelota y portería propia
    /// </summary>
    private void ExecuteDefending(float[] aiMovement, float[] rawInputs)
    {
        Vector3 myPos   = transform.position;
        Vector3 ballPos = Bola.instance != null ? Bola.instance.transform.position : myPos;
        Vector3 ownGoal = ownGoalTransform != null ? ownGoalTransform.position : myPos;

        float distToBall = Vector3.Distance(myPos, ballPos);

        // Determinar quién tiene la pelota
        bool ballIsFree = (Bola.instance == null || !Bola.instance.EnPosesion || Bola.instance.Owner == null);
        bool teamHasBall = false;
        bool iHaveBall   = false;

        if (!ballIsFree)
        {
            PlayerID ownerID = Bola.instance.Owner.GetComponent<PlayerID>();
            if (ownerID != null)
            {
                teamHasBall = (ownerID.id % 2 == myPlayer.id % 2);
                iHaveBall   = (ownerID == myPlayer);
            }
        }

        Vector3 moveDir = Vector3.zero;

        if (iHaveBall)
        {
            // YO TENGO LA PELOTA defendiendo → pasar a un compañero para sacarla del área
            if (Time.time >= nextActionTime && Pase.instance != null)
            {
                Pase.instance.searchPlayersToPass("npc", transform.position, myPlayer.id);
                nextActionTime = Time.time + actionCooldown;
                lastActionDetail = "DEFENDIENDO: Con pelota → ¡PASE para sacar del área!";
                if (enableDebugLogs)
                    Debug.Log($"[FSM {myPlayer.id}] ⚽ PASE defensivo ejecutado");
            }
            else
            {
                lastActionDetail = "DEFENDIENDO: Con pelota → esperando cooldown para pasar";
            }

            // Mientras tanto, avanzar hacia el campo rival para alejarse de la portería propia
            if (rivalGoalTransform != null)
            {
                moveDir = (rivalGoalTransform.position - myPos);
                moveDir.y = 0f;
                moveDir = moveDir.normalized;
            }
            else
            {
                moveDir = new Vector3(aiMovement[0], 0f, aiMovement[1]).normalized;
            }
        }
        else if (ballIsFree)
        {
            // PELOTA LIBRE: correr directamente a por ella
            moveDir = (ballPos - myPos);
            moveDir.y = 0f;
            moveDir = moveDir.normalized;
            lastActionDetail = $"DEFENDIENDO: Pelota libre → recuperando (dist={distToBall:F1})";
        }
        else if (!teamHasBall)
        {
            // RIVAL tiene la pelota: correr hacia la pelota para robarla
            moveDir = (ballPos - myPos);
            moveDir.y = 0f;
            moveDir = moveDir.normalized;
            lastActionDetail = $"DEFENDIENDO: Rival con pelota → persiguiendo (dist={distToBall:F1})";
        }

        ApplyMovement(moveDir);
    }

    /// <summary>
    /// Estado ATACANDO: Lógica adaptada de Delantero.cs
    /// - Si tiene la pelota: avanza hacia la portería rival
    /// - Si no tiene la pelota: sube al campo rival para apoyar / recibir pase
    /// </summary>
    private void ExecuteAttacking(float[] aiMovement, float[] rawInputs)
    {
        bool iHaveBall = HasBallControl();
        Vector3 myPos = transform.position;

        Vector3 moveDir;

        if (iHaveBall && rivalGoalTransform != null)
        {
            // Con pelota: ir hacia la portería rival
            Vector3 goalPos = rivalGoalTransform.position;
            moveDir = (goalPos - myPos);
            moveDir.y = 0f;
            moveDir = moveDir.normalized;
            lastActionDetail = $"ATACANDO: Con pelota → portería rival (dist={Vector3.Distance(myPos, goalPos):F1})";
        }
        else
        {
            // Sin pelota: SIEMPRE avanzar hacia el campo rival para apoyar
            UpdateAttackTarget();

            if (attackTarget != Vector3.zero)
            {
                moveDir = (attackTarget - myPos);
                moveDir.y = 0f;

                // Si ya llegó al target, pedir uno nuevo
                if (moveDir.sqrMagnitude < attackRetargetDistance * attackRetargetDistance)
                {
                    attackTarget = Vector3.zero; // Forzar recalcular en el próximo frame
                }

                moveDir = moveDir.normalized;
                lastActionDetail = $"ATACANDO: Subiendo a campo rival (dist target={Vector3.Distance(myPos, attackTarget):F1})";
            }
            else
            {
                // Fallback: ir directamente hacia la portería rival
                if (rivalGoalTransform != null)
                {
                    moveDir = (rivalGoalTransform.position - myPos);
                    moveDir.y = 0f;
                    moveDir = moveDir.normalized;
                    lastActionDetail = "ATACANDO: Avanzando hacia portería rival (fallback)";
                }
                else
                {
                    moveDir = new Vector3(aiMovement[0], 0f, aiMovement[1]).normalized;
                    lastActionDetail = "ATACANDO: Movimiento IA (sin referencias)";
                }
            }
        }

        ApplyMovement(moveDir);
    }

    /// <summary>
    /// Estado PASANDO: Ejecuta el pase usando Pase.cs
    /// </summary>
    private void ExecutePassing(float[] aiMovement)
    {
        if (!HasBallControl())
        {
            // Sin pelota: correr hacia la pelota para poder pasarla
            lastActionDetail = "PASANDO: Sin pelota → corriendo hacia pelota";
            RunToBall();
            return;
        }
        if (Time.time < nextActionTime)
        {
            // En cooldown: usar movimiento IA mientras espera
            lastActionDetail = "PASANDO: Esperando cooldown (moviéndose)";
            ApplyMovement(new Vector3(aiMovement[0], 0f, aiMovement[1]).normalized);
            return;
        }

        if (Pase.instance != null)
        {
            Pase.instance.searchPlayersToPass("npc", transform.position, myPlayer.id);
            nextActionTime = Time.time + actionCooldown;
            lastActionDetail = "PASANDO: ¡PASE EJECUTADO!";
            if (enableDebugLogs)
                Debug.Log($"[FSM {myPlayer.id}] ⚽ PASE ejecutado");
        }
    }

    /// <summary>
    /// Estado TIRANDO: Ejecuta el disparo usando Shoot.cs
    /// </summary>
    private void ExecuteShooting(float[] aiMovement)
    {
        if (!HasBallControl())
        {
            // Sin pelota: correr hacia la pelota para poder disparar
            lastActionDetail = "TIRANDO: Sin pelota → corriendo hacia pelota";
            RunToBall();
            return;
        }
        if (Time.time < nextActionTime)
        {
            // En cooldown: avanzar hacia portería rival mientras espera
            lastActionDetail = "TIRANDO: Esperando cooldown (avanzando)";
            if (rivalGoalTransform != null)
            {
                Vector3 dir = (rivalGoalTransform.position - transform.position);
                dir.y = 0f;
                ApplyMovement(dir.normalized);
            }
            return;
        }

        // Rotar hacia la portería rival antes de disparar
        if (rivalGoalTransform != null)
        {
            Vector3 dir = (rivalGoalTransform.position - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        if (Shoot.instance != null)
        {
            Shoot.instance.disparoLibre();
            nextActionTime = Time.time + actionCooldown;
            lastActionDetail = "TIRANDO: ¡DISPARO EJECUTADO!";
            if (enableDebugLogs)
                Debug.Log($"[FSM {myPlayer.id}] 🎯 DISPARO ejecutado");
        }
    }

    // ========================================================================
    // MOVIMIENTO
    // ========================================================================

    private void ApplyMovement(Vector3 moveDir)
    {
        if (moveDir.sqrMagnitude < 0.001f)
        {
            myRigidbody.linearVelocity = new Vector3(0f, myRigidbody.linearVelocity.y, 0f);
            return;
        }

        Vector3 movement = moveDir * moveSpeed;
        myRigidbody.linearVelocity = new Vector3(movement.x, myRigidbody.linearVelocity.y, movement.z);

        // Rotación suave hacia la dirección de movimiento
        Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
        myRigidbody.MoveRotation(
            Quaternion.RotateTowards(myRigidbody.rotation, targetRot, turnSpeedDeg * Time.deltaTime)
        );
    }

    /// <summary>
    /// Correr directamente hacia la pelota. Usado como fallback cuando el NPC
    /// está en un estado de acción (Pasando/Tirando) pero no tiene la pelota.
    /// </summary>
    private void RunToBall()
    {
        if (Bola.instance == null) return;

        Vector3 dir = Bola.instance.transform.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.001f) return;

        ApplyMovement(dir.normalized);
    }

    /// <summary>
    /// Genera un punto aleatorio en el área rival para el comportamiento de ataque.
    /// Adaptado de goToOtherArea de Delantero.cs, usando ZoneManager.
    /// </summary>
    private void UpdateAttackTarget()
    {
        // Si ya tenemos un target válido y estamos lejos, seguir con él
        if (attackTarget != Vector3.zero &&
            Vector3.Distance(transform.position, attackTarget) > attackRetargetDistance)
            return;

        // Intentar conseguir un punto aleatorio en el área rival
        if (ZoneManager.instance != null)
        {
            int rivalTeam = (myPlayer.id % 2 == 0) ? 1 : 0;
            var areas = ZoneManager.instance.getAreasByTeam(rivalTeam);
            if (areas != null && areas.Count > 0)
            {
                Vector3 randomPoint = ZoneManager.instance.GetRandomPointInSelectedAreas(areas, transform.position.y);
                if (randomPoint != Vector3.zero)
                {
                    attackTarget = randomPoint;
                    return;
                }
            }
        }

        // Fallback: apuntar hacia la portería rival
        if (rivalGoalTransform != null)
        {
            attackTarget = rivalGoalTransform.position;
        }
    }

    // ========================================================================
    // INFERENCIA
    // ========================================================================

    private (float[] stateLogits, float[] movement) Predecir(float[] inputFeatures)
    {
        using var inputTensor = new Unity.InferenceEngine.Tensor<float>(
            new Unity.InferenceEngine.TensorShape(1, inputFeatures.Length), inputFeatures);

        worker.Schedule(inputTensor);

        // Cabeza 1: state_logits (4 valores)
        using var stateTensor = worker.PeekOutput("state_logits") as Unity.InferenceEngine.Tensor<float>;
        // Cabeza 2: continuous_actions (2 valores: InputX, InputZ)
        using var movementTensor = worker.PeekOutput("continuous_actions") as Unity.InferenceEngine.Tensor<float>;

        float[] stateLogits = stateTensor != null ? stateTensor.DownloadToArray() : new float[NumStates];
        float[] movement    = movementTensor != null ? movementTensor.DownloadToArray() : new float[2];

        return (stateLogits, movement);
    }

    // ========================================================================
    // UTILIDADES
    // ========================================================================

    private bool HasBallControl()
    {
        if (Bola.instance == null || !Bola.instance.EnPosesion || Bola.instance.Owner == null || myPlayer == null)
            return false;

        PlayerID ownerID = Bola.instance.Owner.GetComponent<PlayerID>();
        return ownerID != null && ownerID == myPlayer;
    }

    private float[] Softmax(float[] logits)
    {
        float[] result = new float[logits.Length];
        float maxVal = float.MinValue;
        for (int i = 0; i < logits.Length; i++)
            if (logits[i] > maxVal) maxVal = logits[i];

        float sumExp = 0f;
        for (int i = 0; i < logits.Length; i++)
        {
            result[i] = Mathf.Exp(logits[i] - maxVal);
            sumExp += result[i];
        }

        for (int i = 0; i < result.Length; i++)
            result[i] /= sumExp;

        return result;
    }

    private int Argmax(float[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (values[i] > values[best]) best = i;
        return best;
    }

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
            Debug.LogWarning($"[AIControllerFSM] scaler JSON no coincide con {inputs.Length} features.");
            scalerWarningShown = true;
        }
    }

    /// <summary>
    /// Recopila las 40 features del entorno. IDÉNTICA a la de AIController.cs y Recorder.cs.
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
                myHasBall = (ownerID == myPlayer) ? 1 : 0;
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

        // ---- Clasificar jugadores en aliados / rivales ----
        PlayerID[] allPlayers = FindObjectsByType<PlayerID>(FindObjectsSortMode.None);

        var allies  = new List<(float dist, PlayerID p)>();
        var enemies = new List<(float dist, PlayerID p)>();

        foreach (PlayerID p in allPlayers)
        {
            if (myPlayer != null && p.gameObject == myPlayer.gameObject) continue;

            float d = Vector3.Distance(myPos, p.transform.position);

            if (myPlayer != null && p.id % 2 == myPlayer.id % 2)
                allies.Add((d, p));
            else
                enemies.Add((d, p));
        }

        allies.Sort((a, b) => a.dist.CompareTo(b.dist));
        enemies.Sort((a, b) => a.dist.CompareTo(b.dist));

        float distClosestAlly  = allies.Count > 0 ? allies[0].dist : 999f;
        float distClosestEnemy = enemies.Count > 0 ? enemies[0].dist : 999f;

        // ---- Helper: datos relativos de un jugador ----
        (float relPx, float relPz, float dx, float dz) GetPlayerData(List<(float dist, PlayerID p)> list, int index)
        {
            if (index >= list.Count) return (0f, 0f, 0f, 0f);
            PlayerID p = list[index].p;
            Vector3 pos = p.transform.position;
            Rigidbody rb = p.GetComponent<Rigidbody>();
            Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;
            Vector3 dir = vel.magnitude > 0.01f ? vel.normalized : Vector3.zero;

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

        // 40 features en el mismo orden del dataset de entrenamiento
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
