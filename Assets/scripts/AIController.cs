using UnityEngine;
 // Asegúrate de tener instalado el paquete "com.unity.sentis" desde el Package Manager

public class AIController : MonoBehaviour
{
    [Header("AI Model Configuration")]
    [Tooltip("El archivo .onnx que generamos en Python")]
    public Unity.InferenceEngine.ModelAsset onnxModelAsset;
    [Tooltip("El archivo scaler.json exportado en Python para normalizar los inputs")]
    public TextAsset scalerJson;

    [Header("References (Mismas que el Recorder)")]
    public PlayerID myPlayer;
    public Rigidbody myRigidbody;
    public CharacterGV characterGV;
    
    // Componentes de Sentis (Motor de Inferencia de Unity)
    private Unity.InferenceEngine.Model runtimeModel;
    private Unity.InferenceEngine.Worker worker;

    [System.Serializable]
    public class ScalerData
    {
        // Valores extraídos directamente de scaler.json
        public float[] mean = { 296.54327392578125f, 92.27310180664062f, 406.1310119628906f, 56.536861419677734f, 132.017578125f, 0.5537700653076172f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 129.46263122558594f };
        public float[] std = { 194.89263916015625f, 102.24421691894531f, 223.0179443359375f, 73.066162109375f, 196.96641540527344f, 0.640921950340271f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 48.70327377319336f };
    }
    private ScalerData scaler = new ScalerData();

    private void Start()
    {
        if (myPlayer == null) myPlayer = GetComponent<PlayerID>();
        if (myRigidbody == null) myRigidbody = GetComponent<Rigidbody>();
        if (characterGV == null) characterGV = GetComponent<CharacterGV>();

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
                    Debug.Log("[AIController] Variables de normalización leídas (o usando defaults hardcodeados).");
                } catch (System.Exception e) {
                    Debug.LogWarning("[AIController] Fallo al parsear scaler.json, usando defaults. Error: " + e.Message);
                }
            }
            else
            {
                Debug.LogWarning("[AIController] Faltan los parámetros scalerJson. Usando defaults hardcodeados.");
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
        
        // Ejecutar inferencia (El modelo piensa)
        float[] aiDecisions = Predecir(inputs);

        // Extraer salidas (Outputs del modelo en Python). Recordemos que salen 2 valores: [InputX, InputZ]
        float aiInputX = aiDecisions[0];
        float aiInputZ = aiDecisions[1];

        // LOG para ver qué está pensando el modelo
        Debug.Log($"[AIController] Pensando... InputX: {aiInputX:F2}, InputZ: {aiInputZ:F2}");

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
        if (Bola.instance != null && Bola.instance.EnPosesion && Bola.instance.Owner != null)
        {
           PlayerID ownerID = Bola.instance.Owner.GetComponent<PlayerID>();
           if (ownerID != null) {
               hasBallTeam = (ownerID.id % 2 == myPlayer.id % 2) ? 1 : 2; 
           }
        }

        float distToRivalGoal = 0f; 
        float distBallToMyGoal = 0f;
        int scoreT1 = 0; 
        int scoreT2 = 0;

        float distClosestAlly = 999f;
        float distClosestEnemy = 999f;

        PlayerID[] allPlayers = FindObjectsByType<PlayerID>(FindObjectsSortMode.None);
        foreach (PlayerID p in allPlayers)
        {
            if (p == myPlayer) continue;

            float d = Vector3.Distance(myPos, p.transform.position);
            
            if (p.id % 2 == myPlayer.id % 2) // Mismo equipo (ej. pares son equipo 1, impares equipo 2)
            {
                if (d < distClosestAlly) distClosestAlly = d;
            }
            else
            {
                if (d < distClosestEnemy) distClosestEnemy = d;
            }
        }

        // Estas son las 12 características exactas que entrenó el modelo. 
        // El orden es IMPORTANTÍSIMO: debe ser el mismo que las columnas del CSV (de la 1 a la 12, omitiendo tiempo e imputs).
        float[] inputs = {
            myPos.x, myPos.z, 
            ballPos.x, ballPos.z, 
            distToBall, 
            hasBallTeam, 
            distToRivalGoal, 
            scoreT1, scoreT2, 
            distBallToMyGoal, 
            distClosestAlly, 
            distClosestEnemy
        };
        
        // LOG DE DEPURACION: Ver qué le estamos metiendo exactamente a la red
        string inputStr = "Inputs pre-normalizar: " + string.Join(", ", inputs);
        
        // Aplicar normalización (Restar Media y Dividir por Desviación Típica)
        if (scaler != null && scaler.mean != null && scaler.std != null)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = (inputs[i] - scaler.mean[i]) / scaler.std[i];
            }
            inputStr += " | Inputs POST-normalizar: " + string.Join(", ", inputs);
        }
        else 
        {
            inputStr += " | AVISO: SCALER ES NULL. NO SE ESTA NORMALIZANDO.";
        }
        
        Debug.Log(inputStr);
        return inputs;
    }

    /// <summary>
    /// Ejecuta la Red Neuronal
    /// </summary>
    private float[] Predecir(float[] inputFeatures)
    {
        // 1. Crear un Tensor en forma de matriz plana a partir de nuestras variables
        using var inputTensor = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, inputFeatures.Length), inputFeatures);

        // 2. Pasar el input por la red neuronal
        worker.Schedule(inputTensor);

        // 3. Obtener el resultado
        // Nota: Poner el nombre al tensor de output no siempre es necesario si solo tiene uno, lo cogerá por defecto
        using var outputTensor = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;

        // 4. Copiamos el tensor a un array normal de C#
        float[] results = outputTensor.DownloadToArray();

        return results;
    }

    private void OnDestroy()
    {
        // Limpiamos los tensores de memoria para evitar memory leaks
        worker?.Dispose();
    }
}
