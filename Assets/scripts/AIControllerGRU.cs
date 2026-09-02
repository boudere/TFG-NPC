using UnityEngine;
using System.Collections.Generic;

public class AIControllerGRU : MonoBehaviour
{
    [Header("AI Model (GRU)")]
    public Unity.InferenceEngine.ModelAsset onnxModelAsset;
    public TextAsset scalerJson;

    [Header("Action Inference")]
    [Range(0f, 1f)] public float actionThreshold = 0.35f;
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

    [Header("Indicador")]
    [Tooltip("Marca en pantalla cual de los once jugadores lleva el modelo.")]
    public bool mostrarIndicador = true;
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
    private const int ExpectedInputSize = 40;
    private const int HiddenStateSize = 64;
    private float nextActionTime = 0f;
    private bool scalerWarningShown = false;
    private float nextLogTime = 0f;

    private float[] hiddenStateArray;


    // ── Estado del sprint de robo (copiado de AIControllerFNNClasi para que las
    //    tres arquitecturas ejecuten las acciones exactamente igual) ──
    private bool _sprintActivo;
    private Transform _sprintObjetivo;
    private PlayerID _sprintPoseedor;
    private Vector3 _sprintDireccion;
    private Vector3 _sprintOrigen;
    private float _sprintFin;
    private float _sprintDistanciaPermitida;
    private float _sprintMejorDistancia;

    private AIRecorder aiRecorder;
    private Porteria _porteriaPropia;
    private Porteria _porteriaRival;

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

        if (aiRecorder == null) aiRecorder = GetComponent<AIRecorder>();

        // Indicador flotante: este componente solo esta habilitado en el jugador
        // del modelo, asi que el marcador aparece justo sobre el que toca.
        if (mostrarIndicador && GetComponent<IndicadorModelo>() == null)
        {
            IndicadorModelo ind = gameObject.AddComponent<IndicadorModelo>();
            ind.nombreOverride = "GRU";
        }

        hiddenStateArray = new float[HiddenStateSize];

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
        // el dataset se graba con el marcador de verdad, asi que enviar ceros
        // aqui haria que el modelo viera en partida una entrada distinta de la
        // que aprendio. Es el fallo silencioso mas caro de todos.
        if (ownGoalTransform != null)
            _porteriaPropia = ownGoalTransform.GetComponentInParent<Porteria>()
                              ?? ownGoalTransform.GetComponent<Porteria>();
        if (rivalGoalTransform != null)
            _porteriaRival = rivalGoalTransform.GetComponentInParent<Porteria>()
                             ?? rivalGoalTransform.GetComponent<Porteria>();

        if (onnxModelAsset != null)
        {
            var runtimeModel = Unity.InferenceEngine.ModelLoader.Load(onnxModelAsset);
            worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);
            Debug.Log("[AIControllerGRU] Modelo cargado OK.");
        }
        else
        {
            Debug.LogError("[AIControllerGRU] ¡Falta asignar el modelo ONNX en el inspector!");
        }

        if (scalerJson != null)
        {
            try {
                JsonUtility.FromJsonOverwrite(scalerJson.text, scaler);
                Debug.Log("[AIControllerGRU] Variables de normalización leídas.");
            } catch (System.Exception e) {
                Debug.LogWarning("[AIControllerGRU] Fallo al parsear scaler JSON: " + e.Message);
            }
        }
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
        if (myPlayer == null || worker == null) return;

        // Congelacion compartida: mientras el jugador este en un reset (el saque
        // de 4 segundos tras un gol) la IA se queda quieta como los demas.
        if (EnResetCompartido())
        {
            if (_sprintActivo) TerminarSprintRobo(false, "reset del partido");
            if (myRigidbody != null)
                myRigidbody.linearVelocity = new Vector3(0f, myRigidbody.linearVelocity.y, 0f);
            if (characterGV != null) characterGV.enabled = false;
            // El estado oculto se reinicia en cada saque: en el juego rueda sin
            // parar, y un gol es justo donde el pasado deja de ser informativo.
            ResetHiddenState();
            return;
        }

        // Mientras dura el sprint de robo el modelo no conduce: el NPC va a por
        // el poseedor igual que tu con la K.
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

        // Actuacion: cada fotograma, con la ultima decision tomada.
        Vector3 moveDir = new Vector3(_decInputX, 0f, _decInputZ).normalized;
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

        float[] inputs = RecopilarVariablesDelEntorno();
        
        float[] normalizedInputs = (float[])inputs.Clone();
        NormalizeFeatures(normalizedInputs);

        var prediction = Predecir(normalizedInputs);

        float aiInputX = 0f;
        float aiInputZ = 0f;
        
        if (prediction.movement != null && prediction.movement.Length >= 9)
        {
            int bestClass = 0;
            float maxProb = -float.MaxValue;
            for(int i = 0; i < 9; i++)
            {
                if(prediction.movement[i] > maxProb)
                {
                    maxProb = prediction.movement[i];
                    bestClass = i;
                }
            }
            aiInputX = (bestClass / 3) - 1f;
            aiInputZ = (bestClass % 3) - 1f;
        }
        else if (prediction.movement != null && prediction.movement.Length >= 2)
        {
            aiInputX = prediction.movement[0];
            aiInputZ = prediction.movement[1];
        }
        float shootProb = prediction.actions.Length > 0 ? prediction.actions[0] : 0f;
        float passProb  = prediction.actions.Length > 1 ? prediction.actions[1] : 0f;
        float roboKProb = prediction.actions.Length > 2 ? prediction.actions[2] : 0f;
        float roboLProb = prediction.actions.Length > 3 ? prediction.actions[3] : 0f;

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

            float distToGoal = inputs[9];
            float distToBall = inputs[7];
            string teamHasBall = inputs[8] == 1 ? "MI_EQUIPO" : (inputs[8] == 2 ? "RIVAL" : "NADIE");
            float distAlly = inputs[14];
            float distEnemy = inputs[15];

            Debug.Log($"[GRU] Prediccion -> Mov=({aiInputX:F2},{aiInputZ:F2}) | Shoot={shootProb:F3} | Pass={passProb:F3}\n" +
                      $"Contexto -> {ballStatus} | Posesion: {teamHasBall} | DistPelota: {distToBall:F1} | DistPorteria: {distToGoal:F1} | DistAliado: {distAlly:F1} | DistEnemigo: {distEnemy:F1}");
        }

        _decInputX = aiInputX;
        _decInputZ = aiInputZ;
        TryApplyAction(shootProb, passProb, roboKProb, roboLProb);
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
            new Unity.InferenceEngine.TensorShape(1, 1, inputFeatures.Length), inputFeatures);
            
        using var hiddenTensor = new Unity.InferenceEngine.Tensor<float>(
            new Unity.InferenceEngine.TensorShape(1, 1, HiddenStateSize), hiddenStateArray);

        worker.SetInput("vector_observation", inputTensor);
        worker.SetInput("hidden_state_in", hiddenTensor);

        worker.Schedule();

        using var movementTensor = worker.PeekOutput("continuous_actions") as Unity.InferenceEngine.Tensor<float>;
        using var actionsTensor = worker.PeekOutput("discrete_actions") as Unity.InferenceEngine.Tensor<float>;
        using var hiddenOutTensor = worker.PeekOutput("hidden_state_out") as Unity.InferenceEngine.Tensor<float>;

        float[] movement = movementTensor != null ? movementTensor.DownloadToArray() : new float[9];
        float[] actions  = actionsTensor != null ? actionsTensor.DownloadToArray() : new float[4];
        
        if (hiddenOutTensor != null)
        {
            hiddenStateArray = hiddenOutTensor.DownloadToArray();
        }

        return (movement, actions);
    }


    /// <summary>Avisa al grabador de que se acaba de ejecutar una accion.</summary>
    private void AnotarAccion(int accion)
    {
        if (aiRecorder != null) aiRecorder.RegistrarAccion(accion);
    }

    /// <summary>
    /// Consulta TODOS los PlayerID de este GameObject (el rol y el CharacterGV
    /// conviven en el mismo objeto) y devuelve true si alguno esta en reset.
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
            return;

        int miId = myPlayer != null ? myPlayer.id : -1;
        float distBalon = Vector3.Distance(transform.position, Bola.instance.transform.position);
        float umbral = Mathf.Max(WinTheBall.instance.distanciaMaxima, sprintArrivalRadius + 5f);

        if (distBalon <= umbral)
            WinTheBall.instance.EjecutarRobo(gameObject, miId);
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

    // ── ACCIONES ──
    // Las cuatro teclas del jugador humano, no solo dos: sin K ni L el agente
    // no puede recuperar el balon y no seria comparable con el FNN.
    private void TryApplyAction(float shootProb, float passProb, float roboKProb, float roboLProb)
    {
        if (Time.time < nextActionTime) return;

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
                return;
            }

            if (doPass && Pase.instance != null)
            {
                Pase.instance.searchPlayersToPass("npc", transform.position, myPlayer.id);
                AnotarAccion(AIRecorder.ACCION_PASE);
                nextActionTime = Time.time + actionCooldown;
                return;
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
                        return;
                    }

                    if (prefiereK && distPoseedor <= WinTheBall.instance.rangoSprint)
                    {
                        IniciarSprintRobo(owner);
                        AnotarAccion(AIRecorder.ACCION_ROBO_K);
                        nextActionTime = Time.time + actionCooldown;
                    }
                }
            }
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
            Debug.LogWarning($"[AIControllerGRU] scaler.json no coincide con {inputs.Length} features.");
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

        // goalCounterTeam de una porteria = goles ENCAJADOS por su equipo, asi
        // que los mios estan en la porteria rival y viceversa.
        int puntuacionPropia    = _porteriaRival  != null ? _porteriaRival.goalCounterTeam  : 0;
        int puntuacionContraria = _porteriaPropia != null ? _porteriaPropia.goalCounterTeam : 0;

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
    public void ResetHiddenState()
    {
        // Se llama en cada fotograma del saque, asi que se limpia el array en
        // sitio en vez de reservar uno nuevo.
        if (hiddenStateArray == null || hiddenStateArray.Length != HiddenStateSize)
            hiddenStateArray = new float[HiddenStateSize];
        else
            System.Array.Clear(hiddenStateArray, 0, hiddenStateArray.Length);
    }

    private void OnDestroy()
    {
        worker?.Dispose();
    }
}
