using UnityEngine;
using System.Collections.Generic;
// Asegúrate de tener instalado el paquete "com.unity.sentis" desde el Package Manager

public class AIController : MonoBehaviour
{
    public enum BehaviorState
    {
        Recover,
        Approach,
        Shoot,
        Pass
    }

    public enum RolTactico
    {
        Defensa,
        Medio,
        Delantero
    }

    [Header("Tactical Role")]
    [Tooltip("Define cómo se comportará este jugador tácticamente según el ZoneManager")]
    public RolTactico rolTactico = RolTactico.Medio;

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
    [Tooltip("Dispersion angular del disparo en grados (0 = tiro perfecto)")]
    [Range(0f, 30f)] public float shootSpreadDeg = 8f;

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
    [Tooltip("Distancia desde el borde donde empieza la fuerza de repulsion")]
    public float boundaryMargin = 80f;

    [Header("Movement — Seek Steering Behavior")]
    [Tooltip("Velocidad máxima del NPC")]
    public float moveSpeed = 100f;
    [Tooltip("Fuerza máxima de steering (controla la suavidad del giro)")]
    public float maxSteeringForce = 8f;
    [Tooltip("Velocidad de giro en grados/segundo")]
    public float turnSpeedDeg = 540f;
    
    // Componentes de Sentis (Motor de Inferencia de Unity)
    private Unity.InferenceEngine.Worker workerRecover;
    private Unity.InferenceEngine.Worker workerApproach;
    private Unity.InferenceEngine.Worker workerPass;
    private Unity.InferenceEngine.Worker workerShoot;

    private const int ExpectedInputSize = 40;
    private float nextActionTime = 0f;
    private bool scalerWarningShown = false;
    private bool mantenerPosicionDefensiva = false;

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
        float dx = prediction.dx;
        float dz = prediction.dz;
        float shootProb = prediction.shootProb;
        float passProb  = prediction.passProb;

        // --- TÁCTICA: MANTENER POSICIÓN DEFENSIVA ---
        if (mantenerPosicionDefensiva)
        {
            // Forzamos a la IA a quedarse quieta (no persigue ciegamente la pelota)
            dx = 0f;
            dz = 0f;
            shootProb = 0f;
            passProb = 0f;
        }

        // --- APLICAR RESULTADOS AL PERSONAJE (Seek Steering Behavior) ---
        Vector3 moveDir = new Vector3(dx, 0f, dz).normalized;
        ApplyMovement(moveDir);

        // --- Gatillo de acciones ---
        TryApplyAction(shootProb, passProb);

        if (characterGV != null) characterGV.enabled = false;

        // --- Restriccion de Campo (seguridad: clamp suave sin frenar) ---
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

    private void DetermineBehaviorState(float[] rawInputs)
    {
        // Variables crudas relevantes para la toma de decisión basadas en el nuevo array de 40:
        float myHasBall = rawInputs[4];
        float hasBallTeam = rawInputs[8];
        float distToRivalGoal = rawInputs[9];
        float distClosestAlly = rawInputs[14];

        // --- CONSULTA AL ZONE MANAGER ---
        int zonaBalon = -1;
        int equipoBalonZonal = -1; // 0 o 1
        if (ZoneManager.instance != null)
        {
            var zonas = ZoneManager.instance.whereIsBall();
            if (zonas != null && zonas.Count > 0)
            {
                zonaBalon = zonas[0].zona;
                equipoBalonZonal = zonas[0].team;
            }
        }

        int miEquipo = myPlayer != null ? (myPlayer.id % 2) : 0;
        int equipoRival = (miEquipo == 0) ? 1 : 0;

        // Franjas 3 o 4 del rival (Zonas de peligro/ataque)
        bool balonEnRivalAtaque = (equipoBalonZonal == equipoRival && zonaBalon >= 3);
        bool balonEnMiCampo = (equipoBalonZonal == miEquipo);

        // --- DECISIÓN TÁCTICA ---
        if (hasBallTeam != 1f) // Nadie tiene la pelota o la tiene el rival
        {
            if (rolTactico == RolTactico.Defensa && !balonEnMiCampo)
            {
                // El balón está en campo rival, el defensa no sube a presionar.
                mantenerPosicionDefensiva = true; 
                currentState = BehaviorState.Approach; // Usamos approach para que no dispare ni pase
            }
            else
            {
                mantenerPosicionDefensiva = false;
                currentState = BehaviorState.Recover;
            }
        }
        else // MI EQUIPO tiene la pelota
        {
            mantenerPosicionDefensiva = false;

            if (myHasBall > 0.5f) // YO tengo la pelota
            {
                // Disparo Zonal: Dispara si estoy en la zona de ataque del rival, o si estoy lo suficientemente cerca
                if (balonEnRivalAtaque || distToRivalGoal < distanceToShoot)
                    currentState = BehaviorState.Shoot;
                else if (distClosestAlly < distanceToPass)
                    currentState = BehaviorState.Pass;
                else
                    currentState = BehaviorState.Approach;
            }
            else // Un ALIADO tiene la pelota
            {
                if (rolTactico == RolTactico.Defensa && balonEnRivalAtaque)
                {
                    // Si el balón está arriba y un aliado ataca, el defensa se queda atrás
                    mantenerPosicionDefensiva = true;
                    currentState = BehaviorState.Approach;
                }
                else
                {
                    currentState = BehaviorState.Approach;
                }
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

        // 1. Velocidad deseada (del modelo IA)
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
    /// Ejecuta la Red Neuronal.
    /// El modelo exporta 9 probabilidades de movimiento (softmax) y 2 probabilidades de acción (sigmoid).
    /// Decodificamos la clase con mayor probabilidad a (dx, dz) ∈ {-1, 0, 1}².
    /// </summary>
    private (float dx, float dz, float shootProb, float passProb) Predecir(Unity.InferenceEngine.Worker targetWorker, float[] inputFeatures)
    {
        // 1. Crear un Tensor en forma de matriz plana a partir de nuestras variables
        using var inputTensor = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, inputFeatures.Length), inputFeatures);

        // 2. Pasar el input por la red neuronal
        targetWorker.Schedule(inputTensor);

        // 3. Obtener resultados por nombre de salida
        using var movementTensor = targetWorker.PeekOutput("movement_probs") as Unity.InferenceEngine.Tensor<float>;
        using var actionsTensor  = targetWorker.PeekOutput("action_probs")   as Unity.InferenceEngine.Tensor<float>;

        if (movementTensor == null)
        {
            Debug.LogError("[AIController] No se pudo leer output 'movement_probs'.");
            return (0f, 0f, 0f, 0f);
        }

        // 4. Descargar las 9 probabilidades de movimiento y encontrar la clase ganadora (argmax)
        float[] movProbs = movementTensor.DownloadToArray();
        int bestClass = 0;
        float bestProb = movProbs[0];
        for (int i = 1; i < movProbs.Length; i++)
        {
            if (movProbs[i] > bestProb)
            {
                bestProb = movProbs[i];
                bestClass = i;
            }
        }

        // 5. Decodificar clase → (dx, dz): inversa de clase = (dx+1)*3 + (dz+1)
        float dx = (bestClass / 3) - 1f;   // {0,1,2} → {-1, 0, 1}
        float dz = (bestClass % 3) - 1f;   // {0,1,2} → {-1, 0, 1}

        // 6. Probabilidades de acción
        float[] actProbs = actionsTensor != null ? actionsTensor.DownloadToArray() : new float[2];
        float shootProb = actProbs.Length > 0 ? actProbs[0] : 0f;
        float passProb  = actProbs.Length > 1 ? actProbs[1] : 0f;

        return (dx, dz, shootProb, passProb);
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
            AimAtGoal();
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

    /// <summary>
    /// Aim Assist: rota el NPC hacia la porteria rival con dispersion aleatoria.
    /// </summary>
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

    private void OnDestroy()
    {
        // Limpiamos los tensores de memoria para evitar memory leaks
        workerRecover?.Dispose();
        workerApproach?.Dispose();
        workerPass?.Dispose();
        workerShoot?.Dispose();
    }
}
