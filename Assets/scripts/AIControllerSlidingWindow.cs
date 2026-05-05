using UnityEngine;
using System.Collections.Generic;

public class AIControllerSlidingWindow : MonoBehaviour
{
    [Header("AI Model (Sliding Window)")]
    public Unity.InferenceEngine.ModelAsset onnxModelAsset;
    public TextAsset scalerJson;

    [Header("Action Inference")]
    [Range(0f, 1f)] public float actionThreshold = 0.35f;
    public float actionCooldown = 1.0f;
    [Range(0f, 30f)] public float shootSpreadDeg = 8f;

    [Header("Movement")]
    public float moveSpeed = 150f;
    public float maxSteeringForce = 8f;
    public float turnSpeedDeg = 540f;
    [Range(0f, 0.5f)] public float explorationNoise = 0.0f;

    [Header("References")]
    public PlayerID myPlayer;
    public Rigidbody myRigidbody;
    public CharacterGV characterGV;

    [Header("Goals")]
    public Transform rivalGoalTransform;
    public Transform ownGoalTransform;

    [Header("Field Constraints")]
    public bool constrainToField = true;
    public float fieldLimitX = 600f;
    public float fieldLimitZ = 500f;
    public float boundaryMargin = 30f;

    [Header("Debug")]
    public bool enableDebugLogs = false;
    public float logInterval = 1.0f;

    // ── Internals ──
    private Unity.InferenceEngine.Worker worker;
    private const int BaseInputSize = 40;
    private const int ExpectedInputSize = 80;
    private float nextActionTime = 0f;
    private bool scalerWarningShown = false;
    private float nextLogTime = 0f;

    private float[] previousInputs = null;

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

        if (onnxModelAsset != null)
        {
            var runtimeModel = Unity.InferenceEngine.ModelLoader.Load(onnxModelAsset);
            worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);
            Debug.Log("[AIControllerSlidingWindow] Modelo cargado OK.");
        }
        else
        {
            Debug.LogError("[AIControllerSlidingWindow] ¡Falta asignar el modelo ONNX en el inspector!");
        }

        if (scalerJson != null)
        {
            try {
                JsonUtility.FromJsonOverwrite(scalerJson.text, scaler);
                Debug.Log("[AIControllerSlidingWindow] Variables de normalización leídas.");
            } catch (System.Exception e) {
                Debug.LogWarning("[AIControllerSlidingWindow] Fallo al parsear scaler JSON: " + e.Message);
            }
        }
    }

    private void Update()
    {
        if (myPlayer == null || worker == null) return;

        float[] currentInputs = RecopilarVariablesDelEntorno();
        
        if (previousInputs == null)
        {
            previousInputs = (float[])currentInputs.Clone();
        }

        // Concatenar t-1 y t
        float[] combinedInputs = new float[ExpectedInputSize];
        System.Array.Copy(previousInputs, 0, combinedInputs, 0, BaseInputSize);
        System.Array.Copy(currentInputs, 0, combinedInputs, BaseInputSize, BaseInputSize);

        // Guardar current para el proximo frame
        System.Array.Copy(currentInputs, 0, previousInputs, 0, BaseInputSize);

        float[] normalizedInputs = (float[])combinedInputs.Clone();
        NormalizeFeatures(normalizedInputs);

        var prediction = Predecir(normalizedInputs);

        float aiInputX = prediction.movement.Length > 0 ? prediction.movement[0] : 0f;
        float aiInputZ = prediction.movement.Length > 1 ? prediction.movement[1] : 0f;
        float shootProb = prediction.actions.Length > 0 ? prediction.actions[0] : 0f;
        float passProb  = prediction.actions.Length > 1 ? prediction.actions[1] : 0f;

        if (explorationNoise > 0f)
        {
            aiInputX = Mathf.Clamp(aiInputX + Random.Range(-explorationNoise, explorationNoise), -1f, 1f);
            aiInputZ = Mathf.Clamp(aiInputZ + Random.Range(-explorationNoise, explorationNoise), -1f, 1f);
        }

        if (enableDebugLogs && Time.time >= nextLogTime)
        {
            nextLogTime = Time.time + logInterval;
            bool hasBall = HasBallControl();
            string ballStatus = hasBall ? "CON PELOTA" : "sin pelota";
            
            float distToGoal = currentInputs[9];
            float distToBall = currentInputs[7];
            string teamHasBall = currentInputs[8] == 1 ? "MI_EQUIPO" : (currentInputs[8] == 2 ? "RIVAL" : "NADIE");
            float distAlly = currentInputs[14];
            float distEnemy = currentInputs[15];

            Debug.Log($"[SlidingWindow] Prediccion -> Mov=({aiInputX:F2},{aiInputZ:F2}) | Shoot={shootProb:F3} | Pass={passProb:F3}\n" +
                      $"Contexto -> {ballStatus} | Posesion: {teamHasBall} | DistPelota: {distToBall:F1} | DistPorteria: {distToGoal:F1} | DistAliado: {distAlly:F1} | DistEnemigo: {distEnemy:F1}");
        }

        Vector3 moveDir = new Vector3(aiInputX, 0f, aiInputZ).normalized;
        ApplyMovement(moveDir);
        TryApplyAction(shootProb, passProb);

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

        Vector3 desiredVelocity = moveDir.normalized * moveSpeed;

        if (constrainToField && boundaryMargin > 0f)
        {
            Vector3 pos = transform.position;
            Vector3 boundaryForce = Vector3.zero;

            float distToEdgeXPos = fieldLimitX - pos.x;
            float distToEdgeXNeg = fieldLimitX + pos.x;
            if (distToEdgeXPos < boundaryMargin) boundaryForce.x -= (1f - distToEdgeXPos / boundaryMargin);
            if (distToEdgeXNeg < boundaryMargin) boundaryForce.x += (1f - distToEdgeXNeg / boundaryMargin);

            float distToEdgeZPos = fieldLimitZ - pos.z;
            float distToEdgeZNeg = fieldLimitZ + pos.z;
            if (distToEdgeZPos < boundaryMargin) boundaryForce.z -= (1f - distToEdgeZPos / boundaryMargin);
            if (distToEdgeZNeg < boundaryMargin) boundaryForce.z += (1f - distToEdgeZNeg / boundaryMargin);

            desiredVelocity += boundaryForce * moveSpeed;
            if (desiredVelocity.magnitude > moveSpeed)
                desiredVelocity = desiredVelocity.normalized * moveSpeed;
        }

        Vector3 currentVelocity = myRigidbody.linearVelocity;
        currentVelocity.y = 0f;
        Vector3 steering = desiredVelocity - currentVelocity;
        if (steering.magnitude > maxSteeringForce)
            steering = steering.normalized * maxSteeringForce;

        Vector3 newVelocity = currentVelocity + steering;
        if (newVelocity.magnitude > moveSpeed)
            newVelocity = newVelocity.normalized * moveSpeed;

        myRigidbody.linearVelocity = new Vector3(newVelocity.x, myRigidbody.linearVelocity.y, newVelocity.z);

        if (newVelocity.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(newVelocity.normalized, Vector3.up);
            myRigidbody.MoveRotation(Quaternion.RotateTowards(myRigidbody.rotation, targetRot, turnSpeedDeg * Time.deltaTime));
        }
    }

    private (float[] movement, float[] actions) Predecir(float[] inputFeatures)
    {
        using var inputTensor = new Unity.InferenceEngine.Tensor<float>(
            new Unity.InferenceEngine.TensorShape(1, inputFeatures.Length), inputFeatures);

        worker.Schedule(inputTensor);

        using var movementTensor = worker.PeekOutput("continuous_actions") as Unity.InferenceEngine.Tensor<float>;
        using var actionsTensor = worker.PeekOutput("discrete_actions") as Unity.InferenceEngine.Tensor<float>;

        float[] movement = movementTensor != null ? movementTensor.DownloadToArray() : new float[2];
        float[] actions  = actionsTensor != null ? actionsTensor.DownloadToArray() : new float[2];

        return (movement, actions);
    }

    private void TryApplyAction(float shootProb, float passProb)
    {
        if (Time.time < nextActionTime) return;
        if (!HasBallControl()) return;

        bool doShoot = shootProb >= actionThreshold;
        bool doPass  = passProb  >= actionThreshold;

        if (doShoot && doPass)
            doShoot = shootProb >= passProb;

        if (doShoot && Shoot.instance != null)
        {
            AimAtGoal();
            Shoot.instance.disparoLibre();
            nextActionTime = Time.time + actionCooldown;
            return;
        }

        if (doPass && Pase.instance != null)
        {
            Pase.instance.searchPlayersToPass("npc", transform.position, myPlayer.id);
            nextActionTime = Time.time + actionCooldown;
        }
    }

    private bool HasBallControl()
    {
        if (Bola.instance == null || !Bola.instance.EnPosesion || Bola.instance.Owner == null || myPlayer == null)
            return false;
        PlayerID ownerID = Bola.instance.Owner.GetComponent<PlayerID>();
        return ownerID != null && ownerID == myPlayer;
    }

    private void AimAtGoal()
    {
        if (rivalGoalTransform == null) return;
        Vector3 dir = rivalGoalTransform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        float spread = Random.Range(-shootSpreadDeg, shootSpreadDeg);
        dir = Quaternion.Euler(0f, spread, 0f) * dir;
        transform.rotation = Quaternion.LookRotation(dir);
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
            Debug.LogWarning($"[AIControllerSlidingWindow] scaler.json no coincide con {inputs.Length} features.");
            scalerWarningShown = true;
        }
    }

    private float[] RecopilarVariablesDelEntorno()
    {
        Vector3 myPos = transform.position;

        Vector3 rivalGoalPos = rivalGoalTransform != null ? rivalGoalTransform.position : Vector3.zero;
        Vector3 ownGoalPos   = ownGoalTransform != null ? ownGoalTransform.position : Vector3.zero;

        float relRivalGoalX = rivalGoalPos.x - myPos.x;
        float relRivalGoalZ = rivalGoalPos.z - myPos.z;
        float relOwnGoalX = ownGoalPos.x - myPos.x;
        float relOwnGoalZ = ownGoalPos.z - myPos.z;

        Vector3 ballPos  = Bola.instance != null ? Bola.instance.transform.position : Vector3.zero;
        float relBallX   = ballPos.x - myPos.x;
        float relBallZ   = ballPos.z - myPos.z;
        float distToBall = Vector3.Distance(myPos, ballPos);

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

        float distToRivalGoal   = Vector3.Distance(myPos, rivalGoalPos);
        float distToOwnGoal     = Vector3.Distance(myPos, ownGoalPos);
        float distBallToOwnGoal = Vector3.Distance(ballPos, ownGoalPos);

        int scoreT1 = 0;
        int scoreT2 = 0;
        int puntuacionPropia    = myPlayer.id % 2 == 0 ? scoreT1 : scoreT2;
        int puntuacionContraria = myPlayer.id % 2 == 0 ? scoreT2 : scoreT1;

        PlayerID[] allPlayers = FindObjectsByType<PlayerID>(FindObjectsSortMode.None);

        var allies  = new List<(float dist, PlayerID p)>();
        var enemies = new List<(float dist, PlayerID p)>();

        foreach (PlayerID p in allPlayers)
        {
            if (p.gameObject == myPlayer.gameObject) continue;
            float d = Vector3.Distance(myPos, p.transform.position);
            if (p.id % 2 == myPlayer.id % 2) allies.Add((d, p));
            else enemies.Add((d, p));
        }

        allies.Sort((a, b) => a.dist.CompareTo(b.dist));
        enemies.Sort((a, b) => a.dist.CompareTo(b.dist));

        float distClosestAlly  = allies.Count  > 0 ? allies[0].dist  : 999f;
        float distClosestEnemy = enemies.Count > 0 ? enemies[0].dist : 999f;

        (float relPx, float relPz, float dx, float dz) GetPlayerData(List<(float dist, PlayerID p)> list, int index)
        {
            if (index >= list.Count) return (0f, 0f, 0f, 0f);
            PlayerID p  = list[index].p;
            Vector3 pos = p.transform.position;
            Rigidbody rb = p.GetComponent<Rigidbody>();
            Vector3 vel  = rb != null ? rb.linearVelocity : Vector3.zero;
            Vector3 dir  = vel.magnitude > 0.01f ? vel.normalized : Vector3.zero;
            return (pos.x - myPos.x, pos.z - myPos.z, dir.x, dir.z);
        }

        var (a1px, a1pz, a1dx, a1dz) = GetPlayerData(allies, 0);
        var (a2px, a2pz, a2dx, a2dz) = GetPlayerData(allies, 1);
        var (a3px, a3pz, a3dx, a3dz) = GetPlayerData(allies, 2);

        var (e1px, e1pz, e1dx, e1dz) = GetPlayerData(enemies, 0);
        var (e2px, e2pz, e2dx, e2dz) = GetPlayerData(enemies, 1);
        var (e3px, e3pz, e3dx, e3dz) = GetPlayerData(enemies, 2);

        float[] inputs = {
            relRivalGoalX, relRivalGoalZ, relOwnGoalX, relOwnGoalZ,
            myHasBall, relBallX, relBallZ, distToBall, hasBallTeam,
            distToRivalGoal, distToOwnGoal, puntuacionPropia, puntuacionContraria,
            distBallToOwnGoal, distClosestAlly, distClosestEnemy,
            a1px, a1pz, a1dx, a1dz, a2px, a2pz, a2dx, a2dz, a3px, a3pz, a3dx, a3dz,
            e1px, e1pz, e1dx, e1dz, e2px, e2pz, e2dx, e2dz, e3px, e3pz, e3dx, e3dz
        };

        return inputs;
    }
    public void ResetHistory()
    {
        previousInputs = null;
    }

    private void OnDestroy()
    {
        worker?.Dispose();
    }
}
