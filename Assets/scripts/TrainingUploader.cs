using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Logica compartida de entrenamiento: manda el CSV al servidor FastAPI,
/// guarda el ONNX y el scaler en persistentDataPath y avisa a los NPCs que
/// haya en escena para que recarguen el modelo en caliente.
///
/// La usan los dos caminos que entrenan, para que la red y el guardado vivan
/// en un unico sitio y no se separen con el tiempo:
///   - TrainingClient : tecla T, durante el partido.
///   - AddTrain       : pantalla del nombre, al terminar el entrenamiento.
/// </summary>
public static class TrainingUploader
{
    public const string DefaultServerUrl = "http://localhost:8000";

    /// <summary>Segundos que se espera al servidor antes de rendirse.</summary>
    public const int TimeoutSegundos = 600;

    [Serializable]
    private class TrainRequestData
    {
        public string csv_data;
        public string model_name;
    }

    /// <summary>Respuesta cruda del endpoint /train.</summary>
    [Serializable]
    public class TrainResponseData
    {
        public bool success;
        public string model_name;
        public string onnx_base64;
        public string scaler_json;
        // Metricas planas: JsonUtility de Unity no soporta diccionarios.
        public float acc_mov;
        public float prec_mov;
        public float rec_mov;
        public float f1_mov;
        public float acc_shoot;
        public float prec_shoot;
        public float rec_shoot;
        public float f1_shoot;
        public float acc_pass;
        public float prec_pass;
        public float rec_pass;
        public float f1_pass;
        public float loss_final;
        public string message;
    }

    /// <summary>Resultado de alto nivel de un intento de entrenamiento.</summary>
    public class Resultado
    {
        public bool ok;
        public string error;            // null cuando ok
        public TrainResponseData datos; // null cuando !ok
    }

    /// <summary>
    /// Manda el CSV, espera al entrenamiento y guarda los archivos resultantes.
    /// Se ejecuta con StartCoroutine desde cualquier MonoBehaviour.
    /// </summary>
    /// <param name="onProgreso">Mensajes de estado para pintarlos donde haga falta.</param>
    /// <param name="onFin">Se llama una sola vez, con el resultado final.</param>
    public static IEnumerator Entrenar(
        string serverUrl,
        string csvData,
        string modelName,
        Action<string> onProgreso,
        Action<Resultado> onFin)
    {
        Resultado resultado = new Resultado();

        if (string.IsNullOrWhiteSpace(csvData))
        {
            resultado.ok = false;
            resultado.error = "No hay datos que enviar: el CSV esta vacio.";
            if (onFin != null) onFin(resultado);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(serverUrl))
            serverUrl = DefaultServerUrl;

        string jsonBody = JsonUtility.ToJson(new TrainRequestData
        {
            csv_data = csvData,
            model_name = modelName
        });

        string url = serverUrl.TrimEnd('/') + "/train";
        Debug.Log("[TrainingUploader] POST " + url + " - modelo: " + modelName);

        if (onProgreso != null) onProgreso("Entrenando modelo en el servidor...");

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.timeout = TimeoutSegundos;

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                resultado.ok = false;
                resultado.error = "No se pudo conectar al servidor.\n" +
                                  "Comprueba que esta corriendo 'python training_server.py'.\n\n" + www.error;
                Debug.LogError("[TrainingUploader] Error de conexion: " + www.error);
                if (onFin != null) onFin(resultado);
                yield break;
            }

            if (www.result == UnityWebRequest.Result.ProtocolError)
            {
                string errorBody = www.downloadHandler.text;
                resultado.ok = false;
                resultado.error = "Error del servidor (" + www.responseCode + "):\n" + Truncar(errorBody, 200);
                Debug.LogError("[TrainingUploader] Error HTTP " + www.responseCode + ": " + errorBody);
                if (onFin != null) onFin(resultado);
                yield break;
            }

            string responseText = www.downloadHandler.text;
            Debug.Log("[TrainingUploader] Respuesta recibida: " + Truncar(responseText, 200));

            TrainResponseData respuesta;
            try
            {
                respuesta = JsonUtility.FromJson<TrainResponseData>(responseText);
            }
            catch (Exception e)
            {
                resultado.ok = false;
                resultado.error = "Error parseando la respuesta: " + e.Message;
                if (onFin != null) onFin(resultado);
                yield break;
            }

            if (respuesta == null || !respuesta.success)
            {
                resultado.ok = false;
                resultado.error = "Entrenamiento fallido: " + (respuesta != null ? respuesta.message : "respuesta vacia");
                if (onFin != null) onFin(resultado);
                yield break;
            }

            if (onProgreso != null) onProgreso("Guardando modelo...");

            try
            {
                GuardarResultados(respuesta, modelName, ContarFilas(csvData));
                resultado.ok = true;
                resultado.datos = respuesta;
                Debug.Log("[TrainingUploader] Modelo '" + modelName + "' guardado correctamente");
            }
            catch (Exception e)
            {
                resultado.ok = false;
                resultado.error = "Error guardando los archivos: " + e.Message;
                Debug.LogError("[TrainingUploader] Error guardando: " + e);
            }

            if (onFin != null) onFin(resultado);
        }
    }

    /// <summary>
    /// Escribe el ONNX y el scaler en persistentDataPath (la unica ruta que
    /// existe igual en el Editor y en una build, y que no pasa por el pipeline
    /// de assets de Unity) y recarga en caliente los NPCs que haya en escena.
    /// </summary>
    private static int ContarFilas(string csv)
    {
        if (string.IsNullOrEmpty(csv)) return 0;
        int saltos = 0;
        for (int i = 0; i < csv.Length; i++) if (csv[i] == '\n') saltos++;
        return Mathf.Max(0, saltos);   // -1 cabecera, +1 ultima linea sin salto
    }

    private static void GuardarResultados(TrainResponseData respuesta, string modelName, int frames)
    {
        string saveDir = Application.persistentDataPath;
        byte[] onnxBytes = Convert.FromBase64String(respuesta.onnx_base64);

        // 1. Copia con el nombre elegido, como historico.
        string namedOnnxPath = Path.Combine(saveDir, "SoccerModel_" + modelName + ".onnx");
        File.WriteAllBytes(namedOnnxPath, onnxBytes);
        string namedScalerPath = Path.Combine(saveDir, "scaler_" + modelName + ".json");
        File.WriteAllText(namedScalerPath, respuesta.scaler_json);
        Debug.Log("[TrainingUploader] Modelo guardado: " + namedOnnxPath + " (" + onnxBytes.Length + " bytes)");

        // 1b. Metadatos, para que el selector pueda mostrar fecha y metricas
        //     sin tener que abrir el ONNX.
        ModeloMeta meta = new ModeloMeta();
        meta.nombre = modelName;
        meta.fechaISO = DateTime.Now.ToString("o");
        meta.frames = frames;
        meta.accMov = respuesta.acc_mov;
        meta.f1Mov = respuesta.f1_mov;
        meta.accShoot = respuesta.acc_shoot;
        meta.accPass = respuesta.acc_pass;
        meta.lossFinal = respuesta.loss_final;
        ModelosDisponibles.GuardarMeta(meta);

        // 2. Copia "activa": la que cargan los AIControllerFNNClasi por defecto.
        string activeOnnxPath = Path.Combine(saveDir, "SoccerModel_Active.onnx");
        File.WriteAllBytes(activeOnnxPath, onnxBytes);
        string activeScalerPath = Path.Combine(saveDir, "scaler_Active.json");
        File.WriteAllText(activeScalerPath, respuesta.scaler_json);

        // 3. Hot-swap solo de los NPCs que esten REALMENTE en marcha.
        //    Recargar los 12 controladores deshabilitados creaba 12
        //    InferenceSession para nada y provocaba un tiron de frame justo
        //    al terminar un entrenamiento lanzado a mitad de partido. Los
        //    deshabilitados ya cargan el modelo activo en su propio Start.
        AIControllerFNNClasi[] controllers =
            UnityEngine.Object.FindObjectsByType<AIControllerFNNClasi>(FindObjectsSortMode.None);

        int recargados = 0;
        foreach (AIControllerFNNClasi controller in controllers)
        {
            if (controller == null || !controller.isActiveAndEnabled) continue;
            controller.ReloadModel(activeOnnxPath, activeScalerPath);
            recargados++;
        }

        if (recargados > 0)
            Debug.Log("[TrainingUploader] " + recargados + " NPC(s) recargados con '" + modelName + "'.");
    }

    private static string Truncar(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
    }
}
