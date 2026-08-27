using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pantalla de nombre al terminar un entrenamiento (escena 8, AddTrain).
///
/// Entrega el dataset de la partida a TrainingRunner, igual que la tecla T
/// durante el partido. Como el runner sobrevive a los cambios de escena, aqui
/// no hay que esperar a nada: se lanza y se puede volver al menu.
///
/// El CSV lo deja preparado Timer.EndMatch() en Data (que tiene
/// DontDestroyOnLoad) justo antes de cambiar de escena.
/// </summary>
public class AddTrain : MonoBehaviour
{
    [SerializeField] private GameObject infoPanel;
    [SerializeField] private TMP_InputField inputField;

    [Header("Servidor de entrenamiento")]
    [Tooltip("URL del servidor FastAPI. Debe coincidir con la del TrainingClient.")]
    public string serverUrl = TrainingUploader.DefaultServerUrl;

    private enum Estado { Formulario, Bloqueado, Lanzado, Error }

    private Estado _estado = Estado.Formulario;
    private string _mensaje = "";
    private string _nombre = "";

    // GUI
    private GUIStyle _panelStyle, _okStyle, _errorStyle;
    private GUIStyle _tituloStyle, _textoStyle, _botonStyle;
    private Texture2D _bgPanel, _bgOk, _bgError, _bgBoton;
    private bool _estilosListos;

    private const int PANEL_W = 520;

    private void Start()
    {
        // Timer.EndMatch congela el juego con timeScale = 0. En la rama de
        // modo juego lo descongela End.cs, pero en la de entrenamiento no lo
        // hacia nadie y el siguiente partido arrancaba parado.
        Time.timeScale = 1f;

        if (TrainingRunner.HayEntrenamientoEnCurso)
        {
            Bloquear();
            return;
        }

        if (Data.instance != null && Data.instance.framesEntrenamiento <= 0)
        {
            _estado = Estado.Error;
            _mensaje = "No se ha grabado ningun dato en esta partida.\n" +
                       "Acuerdate de activar la grabacion con R durante el entrenamiento.";
        }
    }

    private void Update()
    {
        // Si el entrenamiento que venia del partido termina mientras estamos
        // aqui, se desbloquea sola y ya se puede entrenar con la sesion entera.
        if (_estado == Estado.Bloqueado && !TrainingRunner.HayEntrenamientoEnCurso)
        {
            _estado = Estado.Formulario;
            _mensaje = "";
        }
    }

    private void Bloquear()
    {
        _estado = Estado.Bloqueado;
        _mensaje = "Se esta entrenando '" + TrainingRunner.ModeloEnCurso + "', lanzado durante el partido.\n" +
                   "El servidor solo puede con uno a la vez. Espera a que termine, o vuelve al menu: " +
                   "el entrenamiento sigue en marcha y te avisara al acabar.";
    }

    /// <summary>Boton "aceptar": entrega el dataset al runner.</summary>
    public void aceptar()
    {
        if (_estado == Estado.Bloqueado || _estado == Estado.Lanzado) return;

        if (TrainingRunner.HayEntrenamientoEnCurso)
        {
            Bloquear();
            return;
        }

        string nombre = inputField != null ? inputField.text.Trim() : "";

        if (nombre.Length < 2)
        {
            _estado = Estado.Error;
            _mensaje = "Ponle un nombre de al menos 2 caracteres.";
            return;
        }

        if (Data.instance == null)
        {
            _estado = Estado.Error;
            _mensaje = "No hay Data en escena: no puedo recuperar el dataset.";
            return;
        }

        string csv = Data.instance.csvEntrenamiento;
        int frames = Data.instance.framesEntrenamiento;

        if (string.IsNullOrEmpty(csv) || frames <= 0)
        {
            _estado = Estado.Error;
            _mensaje = "No hay datos grabados que enviar.\n" +
                       "Acuerdate de activar la grabacion con R durante el entrenamiento.";
            return;
        }

        Data.instance.textoPanel = nombre;
        _nombre = nombre;

        if (!TrainingRunner.Get().Lanzar(serverUrl, csv, nombre))
        {
            Bloquear();
            return;
        }

        // Ya esta en manos del runner y hay copia en disco: no hace falta
        // arrastrar el CSV a las siguientes escenas.
        Data.instance.csvEntrenamiento = null;
        Data.instance.framesEntrenamiento = 0;

        _estado = Estado.Lanzado;
        _mensaje = "Entrenando '" + nombre + "' con " + frames + " frames.\n" +
                   "Puedes volver al menu: el aviso de fin aparecera abajo a la derecha, " +
                   "estes en la pantalla que estes.";
    }

    /// <summary>Boton "cancelar": vuelve al menu sin entrenar.</summary>
    public void cancelar()
    {
        Volver();
    }

    private void Volver()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(1);
    }

    // ======================================================================
    // GUI de estado, en IMGUI para no depender de montar nada en la escena.
    // ======================================================================
    private void OnGUI()
    {
        PrepararEstilos();

        if (_estado == Estado.Formulario)
        {
            // Esta escena no tiene ningun boton visible para salir: el unico
            // que existe esta desactivado (m_IsActive = 0) y encima apunta a
            // DetailsSelectPlayer, que no esta aqui.
            DibujarSalida();
            return;
        }

        int alto = 210;
        int px = (Screen.width - PANEL_W) / 2;
        int py = (Screen.height - alto) / 2;

        GUIStyle fondo = _estado == Estado.Lanzado ? _okStyle
                       : _estado == Estado.Error ? _errorStyle
                       : _panelStyle;

        GUI.Box(new Rect(px - 10, py, PANEL_W + 20, alto), "", fondo);

        int cy = py + 16;

        string titulo = _estado == Estado.Lanzado ? "ENTRENAMIENTO LANZADO"
                      : _estado == Estado.Bloqueado ? "YA HAY UN ENTRENAMIENTO EN CURSO"
                      : "NO SE HA PODIDO ENTRENAR";

        GUI.Label(new Rect(px, cy, PANEL_W, 28), titulo, _tituloStyle);
        cy += 36;

        GUI.Label(new Rect(px + 12, cy, PANEL_W - 24, 90), _mensaje, _textoStyle);
        cy += 96;

        if (_estado == Estado.Error)
        {
            int w = (PANEL_W - 36) / 2;
            if (GUI.Button(new Rect(px + 12, cy, w, 32), "Reintentar", _botonStyle))
            {
                _estado = Estado.Formulario;
                _mensaje = "";
            }
            if (GUI.Button(new Rect(px + 24 + w, cy, w, 32), "Volver al menu", _botonStyle))
                Volver();
            return;
        }

        if (GUI.Button(new Rect(px + 12, cy, PANEL_W - 24, 32), "Volver al menu", _botonStyle))
            Volver();
    }

    /// <summary>Salida de emergencia mientras se rellena el nombre.</summary>
    private void DibujarSalida()
    {
        int w = 280;
        int h = 30;
        int x = (Screen.width - w) / 2;
        int y = Screen.height - h - 20;

        if (GUI.Button(new Rect(x, y, w, h), "Volver al menu sin entrenar", _botonStyle))
            Volver();
    }

    private void PrepararEstilos()
    {
        if (_estilosListos) return;

        _bgPanel = Tex(new Color(0.06f, 0.06f, 0.15f, 0.96f));
        _bgOk = Tex(new Color(0.08f, 0.20f, 0.08f, 0.96f));
        _bgError = Tex(new Color(0.25f, 0.06f, 0.06f, 0.96f));
        _bgBoton = Tex(new Color(0.15f, 0.45f, 0.85f, 1f));

        _panelStyle = new GUIStyle(GUI.skin.box);
        _panelStyle.normal.background = _bgPanel;

        _okStyle = new GUIStyle(GUI.skin.box);
        _okStyle.normal.background = _bgOk;

        _errorStyle = new GUIStyle(GUI.skin.box);
        _errorStyle.normal.background = _bgError;

        _tituloStyle = new GUIStyle(GUI.skin.label);
        _tituloStyle.fontSize = 16;
        _tituloStyle.fontStyle = FontStyle.Bold;
        _tituloStyle.alignment = TextAnchor.MiddleCenter;
        _tituloStyle.normal.textColor = Color.white;

        _textoStyle = new GUIStyle(GUI.skin.label);
        _textoStyle.fontSize = 13;
        _textoStyle.wordWrap = true;
        _textoStyle.normal.textColor = new Color(0.88f, 0.88f, 0.88f);

        _botonStyle = new GUIStyle(GUI.skin.button);
        _botonStyle.fontSize = 14;
        _botonStyle.fontStyle = FontStyle.Bold;
        _botonStyle.normal.background = _bgBoton;
        _botonStyle.hover.background = _bgBoton;
        _botonStyle.normal.textColor = Color.white;
        _botonStyle.hover.textColor = Color.white;

        _estilosListos = true;
    }

    private static Texture2D Tex(Color c)
    {
        Texture2D t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
