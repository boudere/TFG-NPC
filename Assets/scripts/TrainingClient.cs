using UnityEngine;

/// <summary>
/// Panel del nombre del modelo durante el partido (tecla T).
///
/// Flujo:
///   1. El jugador graba datos con el Recorder (tecla R).
///   2. Pulsa T: se abre el campo del nombre y se BLOQUEA el teclado de juego
///      (InputLock) para que escribir no mueva al jugador ni dispare acciones.
///   3. Al pulsar ENTRENAR el trabajo pasa a TrainingRunner, que sobrevive a
///      los cambios de escena, se libera el teclado y el panel se cierra.
///      A partir de ahi se sigue jugando con normalidad; el progreso se ve en
///      el indicador discreto de abajo a la derecha.
///
/// Este script ya no espera al servidor ni guarda archivos: de eso se encargan
/// TrainingRunner y TrainingUploader.
/// </summary>
public class TrainingClient : MonoBehaviour
{
    [Header("Server Configuration")]
    [Tooltip("URL del servidor FastAPI de entrenamiento")]
    public string serverUrl = TrainingUploader.DefaultServerUrl;

    [Header("References")]
    [Tooltip("Referencia al Recorder que contiene los datos grabados")]
    public Recorder recorder;

    [Header("Controls")]
    [Tooltip("Tecla para abrir el panel de entrenamiento")]
    public KeyCode trainKey = KeyCode.T;

    private enum ClientState
    {
        Idle,       // nada en pantalla
        InputName,  // escribiendo el nombre (teclado bloqueado)
        Aviso       // mensaje corto que se auto-oculta
    }

    private ClientState _state = ClientState.Idle;
    private string _modelName = "";
    private string _avisoTexto = "";
    private bool _avisoEsError;
    private float _avisoTimer;

    // GUI
    private GUIStyle _panelStyle, _avisoStyle, _avisoErrorStyle;
    private GUIStyle _titleStyle, _labelStyle, _inputStyle;
    private GUIStyle _buttonStyle, _buttonDisabledStyle, _hintStyle;
    private Texture2D _panelBg, _buttonBg, _buttonHoverBg, _buttonDisabledBg;
    private Texture2D _inputBg, _avisoBg, _avisoErrorBg;
    private bool _stylesInitialized;

    private const int PANEL_W = 460;
    private const int PANEL_H_INPUT = 210;
    private const int PANEL_H_AVISO = 130;
    private const float AVISO_DURACION = 6f;

    // ======================================================================
    // CICLO DE VIDA
    // ======================================================================
    private void Start()
    {
        // El Recorder de ESTE jugador. En la escena hay 12 (uno por jugador) y
        // el campo del inspector viene a null en los 12, asi que el antiguo
        // FindFirstObjectByType devolvia uno cualquiera: normalmente uno
        // deshabilitado con cero frames, y el panel decia "no hay datos"
        // mientras el HUD del Recorder real marcaba cientos.
        if (recorder == null)
            recorder = GetComponent<Recorder>();
    }

    /// <summary>
    /// Devuelve el Recorder que realmente tiene datos.
    /// Se resuelve al pulsar T y no en Start, porque en Start todos tienen cero
    /// frames y no hay forma de distinguirlos.
    /// </summary>
    private Recorder ResolverRecorder()
    {
        Recorder propio = recorder != null ? recorder : GetComponent<Recorder>();
        if (propio != null && propio.GetRecordedLineCount() > 0)
            return propio;

        // Ultimo recurso: el jugador controlado puede haber cambiado, asi que
        // nos quedamos con el Recorder que mas frames lleve grabados.
        Recorder[] todos = FindObjectsByType<Recorder>(FindObjectsSortMode.None);
        Recorder mejor = null;
        int max = 0;

        foreach (Recorder r in todos)
        {
            if (r == null) continue;
            int n = r.GetRecordedLineCount();
            if (n > max)
            {
                max = n;
                mejor = r;
            }
        }

        return mejor != null ? mejor : propio;
    }

    private void OnDisable()
    {
        // Red de seguridad: si este componente se desactiva o la escena se
        // descarga con el panel abierto, el teclado no puede quedarse mudo.
        if (_state == ClientState.InputName)
            InputLock.Liberar();
    }

    private void Update()
    {
        // Ojo: aqui se usa Input y no InputLock a proposito. La T tiene que
        // seguir funcionando... pero solo para ABRIR. Mientras se escribe, la
        // 't' es una letra mas del nombre y el panel se cierra con Esc.
        if (_state == ClientState.Idle && Input.GetKeyDown(trainKey))
            AbrirPanel();

        if (_state == ClientState.Aviso && _avisoTimer > 0f)
        {
            _avisoTimer -= Time.unscaledDeltaTime;
            if (_avisoTimer <= 0f) _state = ClientState.Idle;
        }
    }

    private void AbrirPanel()
    {
        if (TrainingRunner.HayEntrenamientoEnCurso)
        {
            MostrarAviso("Ya hay un entrenamiento en curso ('" +
                         TrainingRunner.ModeloEnCurso + "').\n" +
                         "Espera a que termine para lanzar otro.", true);
            return;
        }

        recorder = ResolverRecorder();

        if (recorder == null || recorder.GetRecordedLineCount() <= 0)
        {
            MostrarAviso("No hay datos grabados todavia.\n" +
                         "Pulsa R para empezar a grabar y juega un rato antes de entrenar.", true);
            return;
        }

        _modelName = "";
        _state = ClientState.InputName;
        InputLock.Capturar();
    }

    private void CerrarPanel()
    {
        _state = ClientState.Idle;
        InputLock.Liberar();
    }

    private void MostrarAviso(string texto, bool esError)
    {
        _avisoTexto = texto;
        _avisoEsError = esError;
        _avisoTimer = AVISO_DURACION;
        _state = ClientState.Aviso;
    }

    /// <summary>
    /// Entrega el dataset al runner y devuelve el control al jugador de
    /// inmediato. No se espera al servidor aqui.
    /// </summary>
    private void LanzarEntrenamiento()
    {
        string nombre = _modelName.Trim();
        string csv = recorder.GetRecordedCSV();
        int frames = recorder.GetRecordedLineCount();

        bool lanzado = TrainingRunner.Get().Lanzar(serverUrl, csv, nombre);

        InputLock.Liberar();

        if (lanzado)
            MostrarAviso("Entrenando '" + nombre + "' con " + frames + " frames.\n" +
                         "Puedes seguir jugando: te aviso al terminar.", false);
        else
            MostrarAviso("No se pudo lanzar: ya hay un entrenamiento en curso.", true);
    }

    // ======================================================================
    // GUI
    // ======================================================================
    private void OnGUI()
    {
        if (_state == ClientState.Idle) return;

        InitStyles();
        int px = (Screen.width - PANEL_W) / 2;

        if (_state == ClientState.InputName) DrawInputPanel(px);
        else DrawAvisoPanel(px);
    }

    private void DrawInputPanel(int px)
    {
        // Teclas de control ANTES de dibujar el campo, para que Esc no acabe
        // metiendose en el texto.
        Event e = Event.current;
        bool nombreValido = !string.IsNullOrWhiteSpace(_modelName) && _modelName.Trim().Length >= 2;

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                CerrarPanel();
                e.Use();
                return;
            }

            if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && nombreValido)
            {
                LanzarEntrenamiento();
                e.Use();
                return;
            }
        }

        int py = (Screen.height - PANEL_H_INPUT) / 2;
        GUI.Box(new Rect(px - 10, py, PANEL_W + 20, PANEL_H_INPUT), "", _panelStyle);

        int cy = py + 12;

        GUI.Label(new Rect(px, cy, PANEL_W, 28), "ENTRENAR MODELO", _titleStyle);
        cy += 32;

        int lineCount = recorder != null ? recorder.GetRecordedLineCount() : 0;
        GUI.Label(new Rect(px + 10, cy, PANEL_W - 20, 20),
            "Datos grabados: " + lineCount + " frames", _labelStyle);
        cy += 24;

        GUI.Label(new Rect(px + 10, cy, PANEL_W - 20, 20), "Nombre del modelo:", _labelStyle);
        cy += 22;

        GUI.SetNextControlName("ModelNameField");
        _modelName = GUI.TextField(new Rect(px + 10, cy, PANEL_W - 20, 28), _modelName, 64, _inputStyle);
        GUI.FocusControl("ModelNameField");
        cy += 36;

        GUIStyle btnStyle = nombreValido ? _buttonStyle : _buttonDisabledStyle;
        if (GUI.Button(new Rect(px + 10, cy, PANEL_W - 20, 32), "ENTRENAR", btnStyle) && nombreValido)
        {
            LanzarEntrenamiento();
            return;
        }
        cy += 38;

        GUI.Label(new Rect(px, cy, PANEL_W, 18),
            "Enter para entrenar   |   Esc para cancelar", _hintStyle);
    }

    private void DrawAvisoPanel(int px)
    {
        int py = (Screen.height - PANEL_H_AVISO) / 2;
        GUI.Box(new Rect(px - 10, py, PANEL_W + 20, PANEL_H_AVISO), "",
                _avisoEsError ? _avisoErrorStyle : _avisoStyle);

        int cy = py + 18;

        GUI.Label(new Rect(px, cy, PANEL_W, 26),
            _avisoEsError ? "NO SE PUEDE ENTRENAR" : "ENTRENAMIENTO LANZADO", _titleStyle);
        cy += 32;

        GUI.Label(new Rect(px + 12, cy, PANEL_W - 24, 60), _avisoTexto, _labelStyle);
    }

    private void InitStyles()
    {
        if (_stylesInitialized) return;

        _panelBg = MakeTex(new Color(0.06f, 0.06f, 0.15f, 0.95f));
        _buttonBg = MakeTex(new Color(0.15f, 0.45f, 0.85f, 1f));
        _buttonHoverBg = MakeTex(new Color(0.20f, 0.55f, 0.95f, 1f));
        _buttonDisabledBg = MakeTex(new Color(0.3f, 0.3f, 0.3f, 0.7f));
        _inputBg = MakeTex(new Color(0.12f, 0.12f, 0.22f, 1f));
        _avisoBg = MakeTex(new Color(0.07f, 0.20f, 0.09f, 0.95f));
        _avisoErrorBg = MakeTex(new Color(0.25f, 0.06f, 0.06f, 0.95f));

        _panelStyle = new GUIStyle(GUI.skin.box);
        _panelStyle.normal.background = _panelBg;

        _avisoStyle = new GUIStyle(GUI.skin.box);
        _avisoStyle.normal.background = _avisoBg;

        _avisoErrorStyle = new GUIStyle(GUI.skin.box);
        _avisoErrorStyle.normal.background = _avisoErrorBg;

        _titleStyle = new GUIStyle(GUI.skin.label);
        _titleStyle.fontSize = 16;
        _titleStyle.fontStyle = FontStyle.Bold;
        _titleStyle.alignment = TextAnchor.MiddleCenter;
        _titleStyle.normal.textColor = Color.white;

        _labelStyle = new GUIStyle(GUI.skin.label);
        _labelStyle.fontSize = 13;
        _labelStyle.wordWrap = true;
        _labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

        _inputStyle = new GUIStyle(GUI.skin.textField);
        _inputStyle.fontSize = 15;
        _inputStyle.alignment = TextAnchor.MiddleLeft;
        _inputStyle.normal.background = _inputBg;
        _inputStyle.focused.background = _inputBg;
        _inputStyle.normal.textColor = Color.white;
        _inputStyle.focused.textColor = Color.white;
        _inputStyle.padding = new RectOffset(8, 8, 4, 4);

        _buttonStyle = new GUIStyle(GUI.skin.button);
        _buttonStyle.fontSize = 14;
        _buttonStyle.fontStyle = FontStyle.Bold;
        _buttonStyle.alignment = TextAnchor.MiddleCenter;
        _buttonStyle.normal.background = _buttonBg;
        _buttonStyle.hover.background = _buttonHoverBg;
        _buttonStyle.active.background = _buttonHoverBg;
        _buttonStyle.normal.textColor = Color.white;
        _buttonStyle.hover.textColor = Color.white;
        _buttonStyle.active.textColor = Color.white;

        _buttonDisabledStyle = new GUIStyle(_buttonStyle);
        _buttonDisabledStyle.normal.background = _buttonDisabledBg;
        _buttonDisabledStyle.hover.background = _buttonDisabledBg;
        _buttonDisabledStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);

        _hintStyle = new GUIStyle(GUI.skin.label);
        _hintStyle.fontSize = 11;
        _hintStyle.alignment = TextAnchor.MiddleCenter;
        _hintStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);

        _stylesInitialized = true;
    }

    private static Texture2D MakeTex(Color col)
    {
        Texture2D t = new Texture2D(1, 1);
        t.SetPixel(0, 0, col);
        t.Apply();
        return t;
    }
}
