using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Cliente de entrenamiento para conectar Unity con el servidor FastAPI.
/// Envía los datos CSV grabados por el Recorder al servidor,
/// recibe el modelo ONNX entrenado + scaler, y los guarda en Assets/.
///
/// Flujo:
///   1. El jugador graba datos con el Recorder (tecla R)
///   2. Al pulsar la tecla de entrenamiento aparece un campo para el nombre del modelo
///   3. Al confirmar, se envían los datos al servidor FastAPI
///   4. Se recibe el ONNX + scaler + métricas y se guardan en Assets/
/// </summary>
public class TrainingClient : MonoBehaviour
{
    // ─── Inspector ──────────────────────────────────────────────────────────
    [Header("Server Configuration")]
    [Tooltip("URL del servidor FastAPI de entrenamiento")]
    public string serverUrl = "http://localhost:8000";

    [Header("References")]
    [Tooltip("Referencia al Recorder que contiene los datos grabados")]
    public Recorder recorder;

    [Header("Controls")]
    [Tooltip("Tecla para abrir/cerrar el panel de entrenamiento")]
    public KeyCode trainKey = KeyCode.T;

    // ─── Estado interno ─────────────────────────────────────────────────────
    private enum ClientState
    {
        Idle,           // No se muestra nada
        InputName,      // Mostrando campo de texto para el nombre
        Sending,        // Enviando datos al servidor
        Training,       // Esperando respuesta del servidor
        Success,        // Entrenamiento completado
        Error           // Error en el proceso
    }

    private ClientState _state = ClientState.Idle;
    private string _modelName = "";
    private string _statusMessage = "";
    private string _errorMessage = "";
    private TrainResponseData _lastResult;
    private float _messageTimer = 0f;

    // ─── GUI ────────────────────────────────────────────────────────────────
    private GUIStyle _panelStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _inputStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _buttonDisabledStyle;
    private GUIStyle _metricsStyle;
    private GUIStyle _successStyle;
    private GUIStyle _errorStyle;
    private GUIStyle _hintStyle;
    private Texture2D _panelBg;
    private Texture2D _buttonBg;
    private Texture2D _buttonHoverBg;
    private Texture2D _buttonDisabledBg;
    private Texture2D _inputBg;
    private Texture2D _successBg;
    private Texture2D _errorBg;
    private bool _stylesInitialized = false;

    // ─── Constantes ─────────────────────────────────────────────────────────
    private const int PANEL_W = 460;
    private const int PANEL_H_INPUT = 210;
    private const int PANEL_H_STATUS = 150;
    private const int PANEL_H_RESULT = 380;
    private const float MESSAGE_DURATION = 8f;

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================
    private void Start()
    {
        if (recorder == null)
            recorder = FindFirstObjectByType<Recorder>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(trainKey))
        {
            if (_state == ClientState.Idle)
            {
                // Verificar que hay datos grabados
                if (recorder == null || recorder.GetRecordedLineCount() <= 0)
                {
                    _state = ClientState.Error;
                    _errorMessage = "No hay datos grabados. Usa el Recorder primero.";
                    _messageTimer = MESSAGE_DURATION;
                    return;
                }
                _state = ClientState.InputName;
                _modelName = "";
            }
            else if (_state == ClientState.InputName)
            {
                _state = ClientState.Idle;
            }
            else if (_state == ClientState.Success || _state == ClientState.Error)
            {
                _state = ClientState.Idle;
            }
        }

        // Auto-hide mensajes
        if ((_state == ClientState.Success || _state == ClientState.Error) && _messageTimer > 0f)
        {
            _messageTimer -= Time.unscaledDeltaTime;
            if (_messageTimer <= 0f)
                _state = ClientState.Idle;
        }
    }

    // ========================================================================
    // GUI
    // ========================================================================
    private void InitStyles()
    {
        if (_stylesInitialized) return;

        // Backgrounds
        _panelBg         = MakeTex(new Color(0.06f, 0.06f, 0.15f, 0.95f));
        _buttonBg        = MakeTex(new Color(0.15f, 0.45f, 0.85f, 1f));
        _buttonHoverBg   = MakeTex(new Color(0.20f, 0.55f, 0.95f, 1f));
        _buttonDisabledBg = MakeTex(new Color(0.3f, 0.3f, 0.3f, 0.7f));
        _inputBg         = MakeTex(new Color(0.12f, 0.12f, 0.22f, 1f));
        _successBg       = MakeTex(new Color(0.08f, 0.20f, 0.08f, 0.95f));
        _errorBg         = MakeTex(new Color(0.25f, 0.06f, 0.06f, 0.95f));

        // Panel
        _panelStyle = new GUIStyle(GUI.skin.box);
        _panelStyle.normal.background = _panelBg;

        // Título
        _titleStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
        _titleStyle.normal.textColor = Color.white;

        // Label
        _labelStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 13, richText = true };
        _labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

        // Input
        _inputStyle = new GUIStyle(GUI.skin.textField)
            { fontSize = 15, alignment = TextAnchor.MiddleLeft };
        _inputStyle.normal.background = _inputBg;
        _inputStyle.focused.background = _inputBg;
        _inputStyle.normal.textColor = Color.white;
        _inputStyle.focused.textColor = Color.white;
        _inputStyle.padding = new RectOffset(8, 8, 4, 4);

        // Botón
        _buttonStyle = new GUIStyle(GUI.skin.button)
            { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _buttonStyle.normal.background = _buttonBg;
        _buttonStyle.hover.background  = _buttonHoverBg;
        _buttonStyle.active.background = _buttonHoverBg;
        _buttonStyle.normal.textColor  = Color.white;
        _buttonStyle.hover.textColor   = Color.white;
        _buttonStyle.active.textColor  = Color.white;

        // Botón deshabilitado
        _buttonDisabledStyle = new GUIStyle(_buttonStyle);
        _buttonDisabledStyle.normal.background = _buttonDisabledBg;
        _buttonDisabledStyle.hover.background  = _buttonDisabledBg;
        _buttonDisabledStyle.normal.textColor  = new Color(0.6f, 0.6f, 0.6f);

        // Métricas
        _metricsStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 12, richText = true };
        _metricsStyle.normal.textColor = new Color(0.7f, 0.85f, 1f);

        // Success
        _successStyle = new GUIStyle(GUI.skin.box);
        _successStyle.normal.background = _successBg;

        // Error
        _errorStyle = new GUIStyle(GUI.skin.box);
        _errorStyle.normal.background = _errorBg;

        // Hint
        _hintStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 11, richText = true, alignment = TextAnchor.MiddleCenter };
        _hintStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);

        _stylesInitialized = true;
    }

    private void OnGUI()
    {
        if (_state == ClientState.Idle) return;
        InitStyles();

        int px = (Screen.width - PANEL_W) / 2;

        switch (_state)
        {
            case ClientState.InputName:
                DrawInputPanel(px);
                break;
            case ClientState.Sending:
            case ClientState.Training:
                DrawStatusPanel(px);
                break;
            case ClientState.Success:
                DrawSuccessPanel(px);
                break;
            case ClientState.Error:
                DrawErrorPanel(px);
                break;
        }
    }

    private void DrawInputPanel(int px)
    {
        int py = (Screen.height - PANEL_H_INPUT) / 2;
        GUI.Box(new Rect(px - 10, py, PANEL_W + 20, PANEL_H_INPUT), "", _panelStyle);

        int cy = py + 12;

        GUI.Label(new Rect(px, cy, PANEL_W, 28), "🧠  ENTRENAR MODELO", _titleStyle);
        cy += 32;

        int lineCount = recorder != null ? recorder.GetRecordedLineCount() : 0;
        GUI.Label(new Rect(px + 10, cy, PANEL_W - 20, 20),
            $"<color=#AAAAAA>Datos grabados: <b>{lineCount}</b> frames</color>", _labelStyle);
        cy += 24;

        GUI.Label(new Rect(px + 10, cy, PANEL_W - 20, 20),
            "Nombre del modelo:", _labelStyle);
        cy += 22;

        // Campo de texto — forzar foco
        GUI.SetNextControlName("ModelNameField");
        _modelName = GUI.TextField(new Rect(px + 10, cy, PANEL_W - 20, 28), _modelName, 64, _inputStyle);
        GUI.FocusControl("ModelNameField");
        cy += 36;

        // Botón de enviar
        bool validName = !string.IsNullOrWhiteSpace(_modelName) && _modelName.Trim().Length >= 2;
        GUIStyle btnStyle = validName ? _buttonStyle : _buttonDisabledStyle;

        if (GUI.Button(new Rect(px + 10, cy, PANEL_W - 20, 32), "▶  ENTRENAR", btnStyle) && validName)
        {
            StartTraining();
        }
        cy += 38;

        GUI.Label(new Rect(px, cy, PANEL_W, 18),
            $"<color=#666666>[{trainKey}] Cancelar</color>", _hintStyle);
    }

    private void DrawStatusPanel(int px)
    {
        int py = (Screen.height - PANEL_H_STATUS) / 2;
        GUI.Box(new Rect(px - 10, py, PANEL_W + 20, PANEL_H_STATUS), "", _panelStyle);

        int cy = py + 20;

        string dots = new string('.', (int)(Time.unscaledTime * 2f) % 4);
        GUI.Label(new Rect(px, cy, PANEL_W, 28),
            $"⏳  ENTRENANDO '{_modelName}'{dots}", _titleStyle);
        cy += 36;

        GUI.Label(new Rect(px + 10, cy, PANEL_W - 20, 20),
            $"<color=#AABBDD>{_statusMessage}</color>", _labelStyle);
        cy += 24;

        GUI.Label(new Rect(px, cy, PANEL_W, 18),
            "<color=#888888>Esto puede tardar unos minutos...</color>", _hintStyle);
    }

    private void DrawSuccessPanel(int px)
    {
        int panelH = _lastResult != null ? PANEL_H_RESULT : PANEL_H_STATUS;
        int py = (Screen.height - panelH) / 2;
        GUI.Box(new Rect(px - 10, py, PANEL_W + 20, panelH), "", _successStyle);

        int cy = py + 12;

        GUI.Label(new Rect(px, cy, PANEL_W, 28),
            $"<color=#55DD66>✓  MODELO '{_modelName}' ENTRENADO</color>", _titleStyle);
        cy += 32;

        if (_lastResult != null)
        {
            GUI.Label(new Rect(px + 10, cy, PANEL_W - 20, 20),
                "📊  MÉTRICAS:", _labelStyle);
            cy += 24;

            DrawMetricRow(px + 20, ref cy, "Movimiento",
                $"Acc={_lastResult.acc_mov:F2}%  F1={_lastResult.f1_mov:F2}%");
            DrawMetricRow(px + 20, ref cy, "Disparo",
                $"Acc={_lastResult.acc_shoot:F2}%  F1={_lastResult.f1_shoot:F2}%");
            DrawMetricRow(px + 20, ref cy, "Pase",
                $"Acc={_lastResult.acc_pass:F2}%  F1={_lastResult.f1_pass:F2}%");
            DrawMetricRow(px + 20, ref cy, "Loss Final",
                $"{_lastResult.loss_final:F4}");

            cy += 10;
            GUI.Label(new Rect(px + 10, cy, PANEL_W - 20, 20),
                "📁  ARCHIVOS GUARDADOS:", _labelStyle);
            cy += 22;
            GUI.Label(new Rect(px + 20, cy, PANEL_W - 40, 18),
                $"<color=#AABBDD>• SoccerModel_{_modelName}.onnx</color>", _metricsStyle);
            cy += 20;
            GUI.Label(new Rect(px + 20, cy, PANEL_W - 40, 18),
                $"<color=#AABBDD>• scaler_{_modelName}.json</color>", _metricsStyle);
            cy += 28;
        }

        GUI.Label(new Rect(px, cy, PANEL_W, 18),
            $"<color=#666666>[{trainKey}] Cerrar</color>", _hintStyle);
    }

    private void DrawErrorPanel(int px)
    {
        int py = (Screen.height - PANEL_H_STATUS) / 2;
        GUI.Box(new Rect(px - 10, py, PANEL_W + 20, PANEL_H_STATUS), "", _errorStyle);

        int cy = py + 20;

        GUI.Label(new Rect(px, cy, PANEL_W, 28),
            "<color=#FF5544>✗  ERROR</color>", _titleStyle);
        cy += 32;

        GUI.Label(new Rect(px + 10, cy, PANEL_W - 20, 40),
            $"<color=#FFAAAA>{_errorMessage}</color>", _labelStyle);
        cy += 44;

        GUI.Label(new Rect(px, cy, PANEL_W, 18),
            $"<color=#666666>[{trainKey}] Cerrar</color>", _hintStyle);
    }

    private void DrawMetricRow(int x, ref int cy, string label, string value)
    {
        GUI.Label(new Rect(x, cy, PANEL_W - 40, 18),
            $"<color=#CCDDFF>{label}:</color>  <color=#FFFFFF><b>{value}</b></color>", _metricsStyle);
        cy += 20;
    }

    // ========================================================================
    // LÓGICA DE ENTRENAMIENTO
    // ========================================================================
    private void StartTraining()
    {
        _state = ClientState.Sending;
        _statusMessage = "Preparando datos...";
        StartCoroutine(SendTrainingRequest());
    }

    private IEnumerator SendTrainingRequest()
    {
        // Obtener CSV del Recorder
        string csvData = recorder.GetRecordedCSV();
        _statusMessage = $"Enviando {recorder.GetRecordedLineCount()} frames al servidor...";
        yield return null;  // Un frame para que se actualice la GUI

        // Construir el JSON del request
        string jsonBody = JsonUtility.ToJson(new TrainRequestData
        {
            csv_data = csvData,
            model_name = _modelName.Trim()
        });

        // Crear la petición HTTP
        string url = $"{serverUrl.TrimEnd('/')}/train";
        Debug.Log($"[TrainingClient] POST {url} — modelo: {_modelName}");

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.timeout = 600;  // 10 minutos máximo para el entrenamiento

            _state = ClientState.Training;
            _statusMessage = "Entrenando modelo en el servidor...";

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                _state = ClientState.Error;
                _errorMessage = $"No se pudo conectar al servidor.\n¿Está corriendo 'python training_server.py'?\n\n{www.error}";
                _messageTimer = MESSAGE_DURATION;
                Debug.LogError($"[TrainingClient] Error de conexión: {www.error}");
                yield break;
            }

            if (www.result == UnityWebRequest.Result.ProtocolError)
            {
                string errorBody = www.downloadHandler.text;
                _state = ClientState.Error;
                _errorMessage = $"Error del servidor ({www.responseCode}):\n{TruncateString(errorBody, 200)}";
                _messageTimer = MESSAGE_DURATION;
                Debug.LogError($"[TrainingClient] Error HTTP {www.responseCode}: {errorBody}");
                yield break;
            }

            // Parsear respuesta
            string responseText = www.downloadHandler.text;
            Debug.Log($"[TrainingClient] Respuesta recibida: {TruncateString(responseText, 200)}");

            TrainResponseData response;
            try
            {
                response = JsonUtility.FromJson<TrainResponseData>(responseText);
            }
            catch (Exception e)
            {
                _state = ClientState.Error;
                _errorMessage = $"Error parseando respuesta: {e.Message}";
                _messageTimer = MESSAGE_DURATION;
                yield break;
            }

            if (!response.success)
            {
                _state = ClientState.Error;
                _errorMessage = $"Entrenamiento fallido: {response.message}";
                _messageTimer = MESSAGE_DURATION;
                yield break;
            }

            // Guardar archivos
            try
            {
                SaveTrainingResults(response);
                _lastResult = response;
                _state = ClientState.Success;
                _messageTimer = MESSAGE_DURATION;
                Debug.Log($"[TrainingClient] ✓ Modelo '{_modelName}' guardado exitosamente");
            }
            catch (Exception e)
            {
                _state = ClientState.Error;
                _errorMessage = $"Error guardando archivos: {e.Message}";
                _messageTimer = MESSAGE_DURATION;
                Debug.LogError($"[TrainingClient] Error guardando: {e}");
            }
        }
    }

    private void SaveTrainingResults(TrainResponseData response)
    {
        string assetsPath = Application.dataPath;

        // 1. Guardar ONNX
        byte[] onnxBytes = Convert.FromBase64String(response.onnx_base64);
        string onnxPath = Path.Combine(assetsPath, $"SoccerModel_{_modelName}.onnx");
        File.WriteAllBytes(onnxPath, onnxBytes);
        Debug.Log($"[TrainingClient] ONNX guardado: {onnxPath} ({onnxBytes.Length} bytes)");

        // 2. Guardar scaler (raw JSON string del servidor)
        string scalerPath = Path.Combine(assetsPath, $"scaler_{_modelName}.json");
        File.WriteAllText(scalerPath, response.scaler_json);
        Debug.Log($"[TrainingClient] Scaler guardado: {scalerPath}");
    }

    // ========================================================================
    // UTILIDADES
    // ========================================================================
    private static Texture2D MakeTex(Color col)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, col);
        t.Apply();
        return t;
    }

    private static string TruncateString(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
    }

    // ========================================================================
    // CLASES DE DATOS (para JSON serialización)
    // ========================================================================
    [Serializable]
    private class TrainRequestData
    {
        public string csv_data;
        public string model_name;
    }

    [Serializable]
    public class TrainResponseData
    {
        public bool success;
        public string model_name;
        public string onnx_base64;
        public string scaler_json;  // JSON raw del scaler, se guarda tal cual
        // Métricas planas (mismo nivel, no anidadas)
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
}
