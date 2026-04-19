using UnityEngine;
using System.Collections.Generic;
// Asegúrate de tener instalado el paquete "com.unity.sentis" desde el Package Manager

public class AIController : MonoBehaviour
{
    public enum BehaviorState
    {
        Recover,
        Approach,
        Pass,
        Shoot
    }

    [Header("AI Models (Mixture of Experts)")]
    public Unity.InferenceEngine.ModelAsset modelRecover;
    public Unity.InferenceEngine.ModelAsset modelApproach;
    public Unity.InferenceEngine.ModelAsset modelPass;
    public Unity.InferenceEngine.ModelAsset modelShoot;

    [Tooltip("El archivo scaler.json exportado en Python para normalizar los inputs")]
    public TextAsset scalerJson;

    [Header("Behavior Thresholds")]
    [Tooltip("Distancia maxima a la porteria rival para considerar disparar")]
    public float distanceToShoot = 15f;
    [Tooltip("Distancia maxima a un aliado para considerar pasar")]
    public float distanceToPass = 10f;

    [Header("Action Inference")]
    [Tooltip("Umbral para activar Disparo/Pase desde la salida sigmoide [0,1]")]
    [Range(0f, 1f)] public float actionThreshold = 0.5f;
    [Tooltip("Tiempo minimo entre acciones para evitar spam")]
    public float actionCooldown = 0.5f;

    [Header("References (Mismas que el Recorder)")]
    public PlayerID myPlayer;
    public Rigidbody myRigidbody;
    public CharacterGV characterGV;

    [Header("Goals")]
    [Tooltip("Si se deja vacio, se buscan automaticamente por componente Porteria")]
    public Transform rivalGoalTransform;
    public Transform ownGoalTransform;

    [Header("Field Constraints (Para evitar Extrapolacion)")]
    [Tooltip("Evita que la IA salga del mapa y se vuelva loca")]
    public bool constrainToField = true;
    public float fieldLimitX = 600f;
    public float fieldLimitZ = 350f;
    
    // Componentes de Sentis (Motor de Inferencia de Unity)
    private Unity.InferenceEngine.Worker workerRecover;
    private Unity.InferenceEngine.Worker workerApproach;
    private Unity.InferenceEngine.Worker workerPass;
    private Unity.InferenceEngine.Worker workerShoot;

    private const int ExpectedInputSize = 40;
    private float nextActionTime = 0f;
    private bool scalerWarningShown = false;

    [Header("Debug")]
    public BehaviorState currentState = BehaviorState.Recover;

    [System.Serializable]
    public class ScalerData
    {
        // Valores extraídos directamente de scaler.json
        public float[] mean = new float[0];
        public float[] std = new float[0];
    }
    private ScalerData scaler = new ScalerData();

    private void Start()
    {
        if (myPlayer == null) myPlayer = GetComponent<PlayerID>();
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

        // Inicializar los 4 modelos
        workerRecover = CreateWorker(modelRecover, "Recover");
        workerApproach = CreateWorker(modelApproach, "Approach");
        workerPass = CreateWorker(modelPass, "Pass");
        workerShoot = CreateWorker(modelShoot, "Shoot");

        if (scalerJson != null)
        {
            try {
                JsonUtility.FromJsonOverwrite(scalerJson.text, scaler);
                Debug.Log("[AIController] Variables de normalizacion leidas desde scaler.json.");
            } catch (System.Exception e) {
                Debug.LogWarning("[AIController] Fallo al parsear scaler.json. Se usara input sin normalizar. Error: " + e.Message);
            }
        }
        else
        {
            Debug.LogWarning("[AIController] Falta scaler.json. Se usara input sin normalizar.");
        }
    }

    private Unity.InferenceEngine.Worker CreateWorker(Unity.InferenceEngine.ModelAsset asset, string name)
    {
        if (asset != null)
        {
            var runtimeModel = Unity.InferenceEngine.ModelLoader.Load(asset);
            var worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);
            Debug.Log($"[AIController] Modelo {name} cargado OK.");
            return worker;
        }
        return null;
    }

    private void Update()
    {
        if (myPlayer == null) return;

        // 1. Recopilar datos crudos del entorno
        float[] rawInputs = RecopilarVariablesDelEntorno();
        if (rawInputs.Length != ExpectedInputSize)
        {
            Debug.LogError($"[AIController] Numero de features invalido: {rawInputs.Length}. Esperado: {ExpectedInputSize}.");
            return;
        }
        
        // 2. Determinar el estado basandose en las reglas heurísticas
        DetermineBehaviorState(rawInputs);

        // 3. Obtener el worker activo para este frame
        Unity.InferenceEngine.Worker activeWorker = GetActiveWorker();
        if (activeWorker == null) 
        {
            // Ocurrirá si no has arrastrado el .onnx al inspector
            return;
        }

        // 4. Normalizar los inputs (Z-Score) justo antes de pasarlos a la IA
        float[] normalizedInputs = (float[])rawInputs.Clone();
        NormalizeFeatures(normalizedInputs);

        // 5. Ejecutar inferencia en el modelo correspondiente
        var prediction = Predecir(activeWorker, normalizedInputs);

        // Salida continua de movimiento: [InputX, InputZ]
        float aiInputX = prediction.movement.Length > 0 ? prediction.movement[0] : 0f;
        float aiInputZ = prediction.movement.Length > 1 ? prediction.movement[1] : 0f;
        // Salida discreta de acciones: [Disparo, Pase]
        float shootProb = prediction.actions.Length > 0 ? prediction.actions[0] : 0f;
        float passProb = prediction.actions.Length > 1 ? prediction.actions[1] : 0f;

        // Ruido de exploracion para evitar que se quede atascado
        float noiseAmount = 0.0f; // Lo ponemos a 0 temporalmente para ver la salida REAL de la red
        aiInputX = Mathf.Clamp(aiInputX + Random.Range(-noiseAmount, noiseAmount), -1f, 1f);
        aiInputZ = Mathf.Clamp(aiInputZ + Random.Range(-noiseAmount, noiseAmount), -1f, 1f);

        // --- TRAZAS DE DEPURACION ---
        // Lo imprimimos cada 30 frames (aprox 2 veces por segundo) para no saturar Unity
        if (Time.frameCount % 30 == 0)
        {
            Debug.Log($"[TFG-DEBUG] --- FRAME {Time.frameCount} ---");
            Debug.Log($"[TFG-DEBUG] 1. ESTADO: {currentState} | myHasBall (Raw[4]): {rawInputs[4]:F1} | hasBallTeam (Raw[8]): {rawInputs[8]:F1}");
            
            // Imprimimos un par de features en crudo y luego normalizadas para ver si el scaler está actuando
            Debug.Log($"[TFG-DEBUG] 2. RAW RelPelotaX: {rawInputs[5]:F2}, RelPelotaZ: {rawInputs[6]:F2} | NORM RelPelotaX: {normalizedInputs[5]:F2}, RelPelotaZ: {normalizedInputs[6]:F2}");
            
            if (scaler == null || scaler.mean.Length == 0)
                Debug.LogWarning("[TFG-DEBUG] CUIDADO: El scaler está VACÍO. Las variables normalizadas son iguales a las crudas. ¡Revisa el inspector!");

            Debug.Log($"[TFG-DEBUG] 3. PREDICCIÓN IA -> InputX: {aiInputX:F2}, InputZ: {aiInputZ:F2} | Prob Shoot: {shootProb:F2}, Prob Pass: {passProb:F2}");
        }

        // --- APLICAR RESULTADOS AL PERSONAJE ---
        float speed = 100f; 
        Vector3 moveDir = new Vector3(aiInputX, 0f, aiInputZ).normalized;
        Vector3 movement = moveDir * speed;
        
        myRigidbody.linearVelocity = new Vector3(movement.x, myRigidbody.linearVelocity.y, movement.z);
        
        if (moveDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            myRigidbody.MoveRotation(Quaternion.RotateTowards(myRigidbody.rotation, targetRot, 540f * Time.deltaTime));
        }

        // --- Gatillo de acciones ---
        TryApplyAction(shootProb, passProb);

        if (characterGV != null) characterGV.enabled = false;

        // --- Restriccion de Campo ---
        if (constrainToField)
        {
            Vector3 pos = transform.position;
            if (Mathf.Abs(pos.x) > fieldLimitX || Mathf.Abs(pos.z) > fieldLimitZ)
            {
                pos.x = Mathf.Clamp(pos.x, -fieldLimitX, fieldLimitX);
                pos.z = Mathf.Clamp(pos.z, -fieldLimitZ, fieldLimitZ);
                transform.position = pos;
                // Frenar al instante para evitar rebotes
                myRigidbody.linearVelocity = Vector3.zero;
            }
        }
    }

    private void DetermineBehaviorState(float[] rawInputs)
    {
        // Variables crudas relevantes para la toma de decisión basadas en el nuevo array de 40:
        // Index 4: myHasBall (0/1)
        // Index 8: hasBallTeam (0=Nadie, 1=Mi equipo, 2=Rival)
        // Index 9: distToRivalGoal
        // Index 14: distClosestAlly

        float myHasBall = rawInputs[4];
        float hasBallTeam = rawInputs[8];
        float distToRivalGoal = rawInputs[9];
        float distClosestAlly = rawInputs[14];

        if (hasBallTeam != 1f) // Nadie tiene la pelota o la tiene el rival
        {
            currentState = BehaviorState.Recover;
        }
        else // MI EQUIPO tiene la pelota (Approach, Pass o Shoot)
        {
            if (myHasBall > 0.5f) // YO tengo la pelota
            {
                if (distToRivalGoal < distanceToShoot)
                {
                    currentState = BehaviorState.Shoot;
                }
                else if (distClosestAlly < distanceToPass)
                {
                    currentState = BehaviorState.Pass;
                }
                else
                {
                    currentState = BehaviorState.Approach;
                }
            }
            else // Un ALIADO tiene la pelota
            {
                currentState = BehaviorState.Approach;
            }
        }
    }

    private Unity.InferenceEngine.Worker GetActiveWorker()
    {
        switch (currentState)
        {
            case BehaviorState.Recover: return workerRecover;
            case BehaviorState.Approach: return workerApproach;
            case BehaviorState.Pass: return workerPass;
            case BehaviorState.Shoot: return workerShoot;
            default: return null;
        }
    }

    private void NormalizeFeatures(float[] inputs)
    {
        if (scaler != null && scaler.mean != null && scaler.std != null && scaler.mean.Length == inputs.Length && scaler.std.Length == inputs.Length)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = (inputs[i] - scaler.mean[i]) / scaler.std[i];
                inputs[i] = Mathf.Clamp(inputs[i], -3f, 3f);
            }
        }
        else if (!scalerWarningShown)
        {
            Debug.LogWarning($"[AIController] scaler.json no coincide con {inputs.Length} features. Se omite normalizacion.");
            scalerWarningShown = true;
        }
    }
    
    /// <summary>
    /// Tiene que ser IDÉNTICA a la recolecta del Recorder.cs
    /// </summary>
    private float[] RecopilarVariablesDelEntorno()
    {
        Vector3 myPos = transform.position;

        // ---- Porterías Relativas ----------------------------------------------------
        Vector3 rivalGoalPos = rivalGoalTransform != null ? rivalGoalTransform.position : Vector3.zero;
        Vector3 ownGoalPos   = ownGoalTransform != null ? ownGoalTransform.position : Vector3.zero;

        float relRivalGoalX = rivalGoalPos.x - myPos.x;
        float relRivalGoalZ = rivalGoalPos.z - myPos.z;
        float relOwnGoalX = ownGoalPos.x - myPos.x;
        float relOwnGoalZ = ownGoalPos.z - myPos.z;

        // ---- Pelota Relativa -------------------------------------------------------
        Vector3 ballPos   = Bola.instance != null ? Bola.instance.transform.position : Vector3.zero;
        float relBallX = ballPos.x - myPos.x;
        float relBallZ = ballPos.z - myPos.z;
        float distToBall  = Vector3.Distance(myPos, ballPos);

        // ---- ¿Quién tiene la pelota? --------------------------------------
        int hasBallTeam  = 0;
        int myHasBall    = 0;
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

        // ---- Métricas ----------------------------------------------------
        float distToRivalGoal = Vector3.Distance(myPos, rivalGoalPos);
        float distToOwnGoal   = Vector3.Distance(myPos, ownGoalPos);
        float distBallToOwnGoal = Vector3.Distance(ballPos, ownGoalPos);

        // ---- Puntuaciones -------------------------------------------------
        int scoreT1 = 0;
        int scoreT2 = 0;
        int puntuacionPropia = myPlayer.id % 2 == 0 ? scoreT1 : scoreT2;
        int puntuacionContraria = myPlayer.id % 2 == 0 ? scoreT2 : scoreT1;

        // ---- Hacia dónde mira el jugador controlado -----------------------
        Vector3 facing = transform.forward;

        // ---- Clasificar todos los jugadores en aliados / rivales ----------
        PlayerID[] allPlayers = FindObjectsByType<PlayerID>(FindObjectsSortMode.None);

        var allies = new List<(float dist, PlayerID p)>();
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

        float distClosestAlly = allies.Count > 0 ? allies[0].dist : 999f;
        float distClosestEnemy = enemies.Count > 0 ? enemies[0].dist : 999f;

        // ---- Helper: obtener información de un jugador RELATIVA -
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

        // 40 features en el mismo orden del dataset de entrenamiento.
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

    /// <summary>
    /// Ejecuta la Red Neuronal
    /// </summary>
    private (float[] movement, float[] actions) Predecir(Unity.InferenceEngine.Worker targetWorker, float[] inputFeatures)
    {
        // 1. Crear un Tensor en forma de matriz plana a partir de nuestras variables
        using var inputTensor = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, inputFeatures.Length), inputFeatures);

        // 2. Pasar el input por la red neuronal
        targetWorker.Schedule(inputTensor);

        // 3. Obtener resultados por nombre de salida
        using var movementTensor = targetWorker.PeekOutput("continuous_actions") as Unity.InferenceEngine.Tensor<float>;
        using var actionsTensor = targetWorker.PeekOutput("discrete_actions") as Unity.InferenceEngine.Tensor<float>;

        if (movementTensor == null)
        {
            Debug.LogError("[AIController] No se pudo leer output 'continuous_actions'.");
            return (new float[2], new float[2]);
        }

        float[] movement = movementTensor.DownloadToArray();
        float[] actions = actionsTensor != null ? actionsTensor.DownloadToArray() : new float[2];

        return (movement, actions);
    }

    private void TryApplyAction(float shootProb, float passProb)
    {
        if (Time.time < nextActionTime) return;
        if (!HasBallControl()) return;

        bool doShoot = shootProb >= actionThreshold;
        bool doPass = passProb >= actionThreshold;

        // Si ambas activan, prioriza la de mayor probabilidad
        if (doShoot && doPass)
            doShoot = shootProb >= passProb;

        if (doShoot && Shoot.instance != null)
        {
            Shoot.instance.disparoLibre();
            nextActionTime = Time.time + actionCooldown;
            return;
        }

        if (doPass && Pase.instance != null)
        {
            // "npc" + id propio para que Pase busque un companero del mismo equipo
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

    private void OnDestroy()
    {
        // Limpiamos los tensores de memoria para evitar memory leaks
        workerRecover?.Dispose();
        workerApproach?.Dispose();
        workerPass?.Dispose();
        workerShoot?.Dispose();
    }
}
