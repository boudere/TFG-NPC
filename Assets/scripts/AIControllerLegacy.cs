using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Controlador IA Legacy: modelo monolítico único (dual-head).
/// Usa un solo ONNX entrenado con PythonTrainingLegacy/train.py.
/// Salidas: continuous_actions (InputX, InputZ) + discrete_actions (Shoot, Pass).
/// Features: 40 variables con posiciones RELATIVAS (formato Recorder actual).
/// </summary>
public class AIControllerLegacy : MonoBehaviour
{
    [Header("AI Model (Legacy — Monolítico)")]
    [Tooltip("Modelo ONNX único exportado por PythonTrainingLegacy/train.py")]
    public Unity.InferenceEngine.ModelAsset onnxModelAsset;

    [Tooltip("El archivo scaler.json exportado en Python para normalizar los inputs")]
    public TextAsset scalerJson;

    [Header("Action Inference")]
    [Tooltip("Umbral para activar Disparo/Pase desde la salida sigmoide [0,1]")]
    [Range(0f, 1f)] public float actionThreshold = 0.5f;
    [Tooltip("Tiempo mínimo entre acciones para evitar spam")]
    public float actionCooldown = 0.5f;

    [Header("Movement")]
    [Tooltip("Velocidad de movimiento del NPC")]
    public float moveSpeed = 150f;
    [Tooltip("Velocidad de giro en grados/segundo")]
    public float turnSpeedDeg = 540f;
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
    public float fieldLimitZ = 350f;

    [Header("Debug")]
    [Tooltip("Activa/desactiva los logs de inferencia en consola")]
    public bool enableDebugLogs = false;
    [Tooltip("Intervalo mínimo entre logs (segundos)")]
    public float logInterval = 1.0f;

    // ── Internals ──
    private Unity.InferenceEngine.Worker worker;
    private const int ExpectedInputSize = 40;
    private float nextActionTime = 0f;
    private bool scalerWarningShown = false;
    private float nextLogTime = 0f;

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
            Debug.Log("[AIControllerLegacy] Modelo Legacy cargado OK.");
        }
        else
        {
            Debug.LogError("[AIControllerLegacy] ¡Falta asignar el modelo ONNX en el inspector!");
        }

        // Cargar scaler
        if (scalerJson != null)
        {
            try {
                JsonUtility.FromJsonOverwrite(scalerJson.text, scaler);
                Debug.Log("[AIControllerLegacy] Variables de normalización leídas desde scaler.json.");
            } catch (System.Exception e) {
                Debug.LogWarning("[AIControllerLegacy] Fallo al parsear scaler JSON: " + e.Message);
            }
        }
        else
        {
            Debug.LogWarning("[AIControllerLegacy] Falta scaler JSON. Se usará input sin normalizar.");
        }
    }

    private void Update()
    {
        if (myPlayer == null || worker == null) return;

        // 1. Recopilar las 40 features (posiciones absolutas — formato Recorder.cs)
        float[] inputs = RecopilarVariablesDelEntorno();
        if (inputs.Length != ExpectedInputSize)
        {
            Debug.LogError($"[AIControllerLegacy] Features inválidas: {inputs.Length}. Esperado: {ExpectedInputSize}.");
            return;
        }

        // 2. Normalizar
        float[] normalizedInputs = (float[])inputs.Clone();
        NormalizeFeatures(normalizedInputs);

        // 3. Ejecutar inferencia
        var prediction = Predecir(normalizedInputs);

        float aiInputX = prediction.movement.Length > 0 ? prediction.movement[0] : 0f;
        float aiInputZ = prediction.movement.Length > 1 ? prediction.movement[1] : 0f;
        float shootProb = prediction.actions.Length > 0 ? prediction.actions[0] : 0f;
        float passProb  = prediction.actions.Length > 1 ? prediction.actions[1] : 0f;

        // 4. Ruido de exploración (opcional)
        if (explorationNoise > 0f)
        {
            aiInputX = Mathf.Clamp(aiInputX + Random.Range(-explorationNoise, explorationNoise), -1f, 1f);
            aiInputZ = Mathf.Clamp(aiInputZ + Random.Range(-explorationNoise, explorationNoise), -1f, 1f);
        }

        // 5. Log periódico con diagnóstico completo
        if (enableDebugLogs && Time.time >= nextLogTime)
        {
            nextLogTime = Time.time + logInterval;
            bool hasBall = HasBallControl();
            string ballStatus = hasBall ? "CON PELOTA" : "sin pelota";
            string shootStatus = shootProb >= actionThreshold ? "ACTIVAR" : $"bajo ({shootProb:F3} < {actionThreshold:F2})";
            string passStatus = passProb >= actionThreshold ? "ACTIVAR" : $"bajo ({passProb:F3} < {actionThreshold:F2})";
            Debug.Log($"[Legacy] Mov=({aiInputX:F2},{aiInputZ:F2}) | {ballStatus} | " +
                      $"Shoot={shootProb:F3} [{shootStatus}] | Pass={passProb:F3} [{passStatus}]");
        }

        // 6. Aplicar movimiento
        Vector3 moveDir = new Vector3(aiInputX, 0f, aiInputZ).normalized;
        ApplyMovement(moveDir);

        // 7. Intentar acciones
        TryApplyAction(shootProb, passProb);

        // 8. Desactivar CharacterGV
        if (characterGV != null) characterGV.enabled = false;

        // 9. Restricción de campo
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

    // ── MOVIMIENTO ──
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

    // ── INFERENCIA ──
    private (float[] movement, float[] actions) Predecir(float[] inputFeatures)
    {
        using var inputTensor = new Unity.InferenceEngine.Tensor<float>(
            new Unity.InferenceEngine.TensorShape(1, inputFeatures.Length), inputFeatures);

        worker.Schedule(inputTensor);

        // continuous_actions → [InputX, InputZ]
        using var movementTensor = worker.PeekOutput("continuous_actions") as Unity.InferenceEngine.Tensor<float>;
        // discrete_actions → [Shoot, Pass] (sigmoide)
        using var actionsTensor = worker.PeekOutput("discrete_actions") as Unity.InferenceEngine.Tensor<float>;

        if (movementTensor == null)
        {
            Debug.LogError("[AIControllerLegacy] No se pudo leer output 'continuous_actions'.");
            return (new float[2], new float[2]);
        }

        float[] movement = movementTensor.DownloadToArray();
        float[] actions  = actionsTensor != null ? actionsTensor.DownloadToArray() : new float[2];

        return (movement, actions);
    }

    // ── ACCIONES ──
    private void TryApplyAction(float shootProb, float passProb)
    {
        if (Time.time < nextActionTime) return;
        if (!HasBallControl()) return;

        bool doShoot = shootProb >= actionThreshold;
        bool doPass  = passProb  >= actionThreshold;

        // Si ambas activan, prioriza la de mayor probabilidad
        if (doShoot && doPass)
            doShoot = shootProb >= passProb;

        if (doShoot && Shoot.instance != null)
        {
            Shoot.instance.disparoLibre();
            nextActionTime = Time.time + actionCooldown;
            if (enableDebugLogs)
                Debug.Log($"[AIControllerLegacy] 🎯 DISPARO ejecutado (prob={shootProb:F2})");
            return;
        }

        if (doPass && Pase.instance != null)
        {
            Pase.instance.searchPlayersToPass("npc", transform.position, myPlayer.id);
            nextActionTime = Time.time + actionCooldown;
            if (enableDebugLogs)
                Debug.Log($"[AIControllerLegacy] ⚽ PASE ejecutado (prob={passProb:F2})");
        }
    }

    private bool HasBallControl()
    {
        if (Bola.instance == null || !Bola.instance.EnPosesion || Bola.instance.Owner == null || myPlayer == null)
            return false;

        PlayerID ownerID = Bola.instance.Owner.GetComponent<PlayerID>();
        return ownerID != null && ownerID == myPlayer;
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
            Debug.LogWarning($"[AIControllerLegacy] scaler.json no coincide con {inputs.Length} features.");
            scalerWarningShown = true;
        }
    }

    /// <summary>
    /// Recopila las 40 features con posiciones RELATIVAS al jugador.
    /// Coincide con el formato del Recorder actual y del train_fsm.py / train.py legacy actualizado.
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

        // 40 features — MISMO ORDEN que el Recorder actual y PythonTrainingLegacy/train.py:
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
