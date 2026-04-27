using UnityEngine;
using System.Collections.Generic;
 // Asegúrate de tener instalado el paquete "com.unity.sentis" desde el Package Manager

public class AIController : MonoBehaviour
{
    [Header("AI Model Configuration")]
    [Tooltip("El archivo .onnx que generamos en Python")]
    public Unity.InferenceEngine.ModelAsset onnxModelAsset;
    [Tooltip("El archivo scaler.json exportado en Python para normalizar los inputs")]
    public TextAsset scalerJson;

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
    
    // Componentes de Sentis (Motor de Inferencia de Unity)
    private Unity.InferenceEngine.Model runtimeModel;
    private Unity.InferenceEngine.Worker worker;
    private const int ExpectedInputSize = 40;
    private float nextActionTime = 0f;
    private bool scalerWarningShown = false;

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

        if (onnxModelAsset != null)
        {
            // Inicializar el modelo ONNX en Unity
            runtimeModel = Unity.InferenceEngine.ModelLoader.Load(onnxModelAsset);
            // Creamos un Worker. Aquí BackendType.GPUCompute usará la gráfica para ir ms rápido. 
            // Si da fallos, puedes cambiarlo a BackendType.CPU
            worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);
            Debug.Log("[AIController] Modelo cargado OK. Worker creado.");
            
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
        else
        {
            Debug.LogError("[AIController] ¡Falta asignar el modelo ONNX en el inspector!");
        }
    }

    private void Update()
    {
        // En update hacemos la recolección de variables, se la pasamos a la IA, y leemos lo que nos manda pulsar
        if (worker == null) {
            Debug.LogWarning("[AIController] Worker es null. Saliendo del Update.");
            return;
        }
        
        float[] inputs = RecopilarVariablesDelEntorno();
        if (inputs.Length != ExpectedInputSize)
        {
            Debug.LogError($"[AIController] Numero de features invalido: {inputs.Length}. Esperado: {ExpectedInputSize}.");
            return;
        }
        
        // Ejecutar inferencia (El modelo piensa)
        var prediction = Predecir(inputs);

        // Salida continua de movimiento: [InputX, InputZ]
        float aiInputX = prediction.movement.Length > 0 ? prediction.movement[0] : 0f;
        float aiInputZ = prediction.movement.Length > 1 ? prediction.movement[1] : 0f;
        // Salida discreta de acciones: [Disparo, Pase]
        float shootProb = prediction.actions.Length > 0 ? prediction.actions[0] : 0f;
        float passProb = prediction.actions.Length > 1 ? prediction.actions[1] : 0f;

        // Ruido de exploracion para evitar que se quede en bucle con la misma accion
        float noiseAmount = 0.25f; // Reduce este valor cuando el modelo funcione bien (0 = sin ruido)
        aiInputX = Mathf.Clamp(aiInputX + Random.Range(-noiseAmount, noiseAmount), -1f, 1f);
        aiInputZ = Mathf.Clamp(aiInputZ + Random.Range(-noiseAmount, noiseAmount), -1f, 1f);

        // LOG para ver que esta pensando el modelo
        Debug.Log($"[AIController] Pensando... InputX: {aiInputX:F2}, InputZ: {aiInputZ:F2}, Disparo: {shootProb:F2}, Pase: {passProb:F2}");

        // --- APLICAR RESULTADOS AL PERSONAJE ---
        // Aquí, en vez de obligar al humano a pulsar botones, forzamos los valores del movimiento
        // Para que `CharacterGV` los lea, o sobreescribimos la velocidad directamente.
        // Vamos a inyectar el movimiento directamente basándonos en cómo funciona tu CharacterGV:
        
        // Nota: asumo que la velocidad del CharacterGV es "speed", usando un valor similar.
        float speed = 150f; 
        Vector3 moveDir = new Vector3(aiInputX, 0f, aiInputZ).normalized;
        Vector3 movement = moveDir * speed;
        
        myRigidbody.linearVelocity = new Vector3(movement.x, myRigidbody.linearVelocity.y, movement.z);
        
        // Girar el personaje hacia donde se mueve
        if (moveDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            myRigidbody.MoveRotation(Quaternion.RotateTowards(myRigidbody.rotation, targetRot, 540f * Time.deltaTime));
        }

        TryApplyAction(shootProb, passProb);

        // --- Deshabilitar los controles manuales de CharacterGV temporalmente si es necesario ---
        if (characterGV != null) characterGV.enabled = false; // Paramos el caracterGV normal para que la IA tome el control total
    }
    
    /// <summary>
    /// Tiene que ser IDÉNTICA a la recolecta del Recorder.cs
    /// </summary>
    private float[] RecopilarVariablesDelEntorno()
    {
        Vector3 myPos = transform.position;
        Vector3 ballPos = Bola.instance != null ? Bola.instance.transform.position : Vector3.zero;

        float distToBall = Vector3.Distance(myPos, ballPos);

        int hasBallTeam = 0;
        int myHasBall = 0;
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

        float distToRivalGoal = rivalGoalTransform != null
            ? Vector3.Distance(myPos, rivalGoalTransform.position) : 0f;
        float distToOwnGoal = ownGoalTransform != null
            ? Vector3.Distance(myPos, ownGoalTransform.position) : 0f;
        float distBallToOwnGoal = ownGoalTransform != null
            ? Vector3.Distance(ballPos, ownGoalTransform.position) : 0f;

        int scoreT1 = 0;
        int scoreT2 = 0;
        int puntuacionPropia = myPlayer.id % 2 == 0 ? scoreT1 : scoreT2;
        int puntuacionContraria = myPlayer.id % 2 == 0 ? scoreT2 : scoreT1;

        Vector3 facing = transform.forward;

        var allies = new List<(float dist, PlayerID p)>();
        var enemies = new List<(float dist, PlayerID p)>();

        PlayerID[] allPlayers = FindObjectsByType<PlayerID>(FindObjectsSortMode.None);
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

        float distClosestAlly = allies.Count > 0 ? allies[0].dist : 999f;
        float distClosestEnemy = enemies.Count > 0 ? enemies[0].dist : 999f;

        (float px, float pz, float dx, float dz) GetPlayerData(List<(float dist, PlayerID p)> list, int index)
        {
            if (index >= list.Count) return (0f, 0f, 0f, 0f);
            PlayerID p = list[index].p;
            Vector3 pos = p.transform.position;
            Rigidbody rb = p.GetComponent<Rigidbody>();
            Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;
            Vector3 dir = vel.magnitude > 0.01f ? vel.normalized : Vector3.zero;
            return (pos.x, pos.z, dir.x, dir.z);
        }

        var (a1px, a1pz, a1dx, a1dz) = GetPlayerData(allies, 0);
        var (a2px, a2pz, a2dx, a2dz) = GetPlayerData(allies, 1);
        var (a3px, a3pz, a3dx, a3dz) = GetPlayerData(allies, 2);

        var (e1px, e1pz, e1dx, e1dz) = GetPlayerData(enemies, 0);
        var (e2px, e2pz, e2dx, e2dz) = GetPlayerData(enemies, 1);
        var (e3px, e3pz, e3dx, e3dz) = GetPlayerData(enemies, 2);

        // 40 features en el mismo orden del dataset de entrenamiento.
        float[] inputs = {
            myPos.x, myPos.z, 
            facing.x, facing.z,
            myHasBall,
            ballPos.x, ballPos.z,
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
        
        // Aplicar normalizacion solo si scaler coincide con el tamano del input.
        if (
            scaler != null &&
            scaler.mean != null && scaler.std != null &&
            scaler.mean.Length == inputs.Length &&
            scaler.std.Length == inputs.Length
        )
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = (inputs[i] - scaler.mean[i]) / scaler.std[i];
                // Clampear a [-3, 3] para evitar que valores fuera de distribucion congelen el modelo
                inputs[i] = Mathf.Clamp(inputs[i], -3f, 3f);
            }
        }
        else if (!scalerWarningShown)
        {
            Debug.LogWarning($"[AIController] scaler.json no coincide con {inputs.Length} features. Se omite normalizacion.");
            scalerWarningShown = true;
        }
        
        return inputs;
    }

    /// <summary>
    /// Ejecuta la Red Neuronal
    /// </summary>
    private (float[] movement, float[] actions) Predecir(float[] inputFeatures)
    {
        // 1. Crear un Tensor en forma de matriz plana a partir de nuestras variables
        using var inputTensor = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, inputFeatures.Length), inputFeatures);

        // 2. Pasar el input por la red neuronal
        worker.Schedule(inputTensor);

        // 3. Obtener resultados por nombre de salida (definidos al exportar ONNX)
        using var movementTensor = worker.PeekOutput("continuous_actions") as Unity.InferenceEngine.Tensor<float>;
        using var actionsTensor = worker.PeekOutput("discrete_actions") as Unity.InferenceEngine.Tensor<float>;

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
        worker?.Dispose();
    }
}
