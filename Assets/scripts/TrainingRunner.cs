using System.Collections;
using UnityEngine;

/// <summary>
/// Ejecuta un entrenamiento en segundo plano y SOBREVIVE a los cambios de
/// escena gracias a DontDestroyOnLoad.
///
/// Esto es lo que permite pulsar T a mitad de partido y seguir jugando: antes
/// la corrutina vivia en el TrainingClient del jugador, dentro de
/// Character.unity, y al terminar el partido la escena se descargaba, el
/// MonoBehaviour moria y la peticion se abortaba a medias. El servidor seguia
/// entrenando sin enterarse y el modelo nunca llegaba a Unity.
///
/// Ademas dibuja el indicador discreto de abajo a la derecha y el aviso de
/// "listo", que se ven en cualquier escena porque este objeto no se destruye.
/// </summary>
public class TrainingRunner : MonoBehaviour
{
    private static TrainingRunner _instance;

    /// <summary>Devuelve el runner, creandolo si hace falta.</summary>
    public static TrainingRunner Get()
    {
        if (_instance != null) return _instance;

        GameObject go = new GameObject("~TrainingRunner");
        _instance = go.AddComponent<TrainingRunner>();
        DontDestroyOnLoad(go);
        return _instance;
    }

    /// <summary>
    /// true si hay un entrenamiento en vuelo. Lo consultan TrainingClient y
    /// AddTrain para no permitir lanzar un segundo: el servidor los serializa
    /// igualmente porque training_server.py bloquea el event loop.
    /// </summary>
    public static bool HayEntrenamientoEnCurso
    {
        get { return _instance != null && _instance.EnCurso; }
    }

    /// <summary>Nombre del modelo que se esta entrenando, o "".</summary>
    public static string ModeloEnCurso
    {
        get { return _instance != null ? _instance._nombre : ""; }
    }

    public bool EnCurso { get; private set; }

    private enum Aviso { Ninguno, Ok, Error }

    private string _nombre = "";
    private float _inicio;
    private string _estadoTexto = "";
    private Aviso _aviso = Aviso.Ninguno;
    private string _avisoTexto = "";
    private string _avisoDetalle = "";
    private float _avisoHasta;

    [Tooltip("Segundos que se queda visible el aviso de fin de entrenamiento.")]
    public float duracionAviso = 12f;

    // GUI
    private GUIStyle _cajaStyle, _cajaOkStyle, _cajaErrorStyle;
    private GUIStyle _tituloStyle, _detalleStyle;
    private Texture2D _bgCaja, _bgOk, _bgError;
    private bool _estilosListos;

    private const int ANCHO = 300;
    private const int MARGEN = 16;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Lanza un entrenamiento. Devuelve false si ya habia uno en marcha.
    /// </summary>
    public bool Lanzar(string serverUrl, string csv, string nombreModelo)
    {
        if (EnCurso) return false;
        if (string.IsNullOrEmpty(csv)) return false;

        _nombre = nombreModelo;
        _inicio = Time.unscaledTime;
        _estadoTexto = "preparando datos";
        EnCurso = true;
        _aviso = Aviso.Ninguno;

        StartCoroutine(Ejecutar(serverUrl, csv, nombreModelo));
        return true;
    }

    private IEnumerator Ejecutar(string serverUrl, string csv, string nombreModelo)
    {
        // Un frame antes del trabajo pesado (JsonUtility sobre un string de
        // varios MB) para que el indicador aparezca antes del tiron.
        yield return null;

        yield return TrainingUploader.Entrenar(
            serverUrl,
            csv,
            nombreModelo,
            msg => _estadoTexto = msg,
            res =>
            {
                EnCurso = false;
                _avisoHasta = Time.unscaledTime + duracionAviso;

                if (res.ok)
                {
                    _aviso = Aviso.Ok;
                    _avisoTexto = "Modelo '" + nombreModelo + "' entrenado";
                    _avisoDetalle = res.datos != null
                        ? "Mov " + res.datos.acc_mov.ToString("F1") + "%   " +
                          "Disparo " + res.datos.acc_shoot.ToString("F1") + "%   " +
                          "Pase " + res.datos.acc_pass.ToString("F1") + "%"
                        : "";
                }
                else
                {
                    _aviso = Aviso.Error;
                    _avisoTexto = "El entrenamiento de '" + nombreModelo + "' fallo";
                    _avisoDetalle = Recortar(res.error, 120);
                }
            });
    }

    private void Update()
    {
        if (_aviso != Aviso.Ninguno && Time.unscaledTime >= _avisoHasta)
            _aviso = Aviso.Ninguno;
    }

    // ======================================================================
    // Indicador discreto, abajo a la derecha. IMGUI para que funcione en
    // cualquier escena sin montar nada.
    // ======================================================================
    private void OnGUI()
    {
        if (!EnCurso && _aviso == Aviso.Ninguno) return;

        PrepararEstilos();

        int alto = EnCurso ? 44 : (string.IsNullOrEmpty(_avisoDetalle) ? 44 : 62);
        int x = Screen.width - ANCHO - MARGEN;
        int y = Screen.height - alto - MARGEN;
        Rect caja = new Rect(x, y, ANCHO, alto);

        GUIStyle fondo = EnCurso ? _cajaStyle
                       : _aviso == Aviso.Ok ? _cajaOkStyle
                       : _cajaErrorStyle;

        GUI.Box(caja, "", fondo);

        if (EnCurso)
        {
            int seg = Mathf.FloorToInt(Time.unscaledTime - _inicio);
            string puntos = new string('.', (int)(Time.unscaledTime * 2f) % 4);

            GUI.Label(new Rect(x + 12, y + 6, ANCHO - 24, 18),
                "Entrenando '" + _nombre + "'" + puntos, _tituloStyle);
            GUI.Label(new Rect(x + 12, y + 24, ANCHO - 24, 16),
                _estadoTexto + "   " + (seg / 60) + ":" + (seg % 60).ToString("00"), _detalleStyle);
            return;
        }

        GUI.Label(new Rect(x + 12, y + 6, ANCHO - 24, 18), _avisoTexto, _tituloStyle);

        if (!string.IsNullOrEmpty(_avisoDetalle))
            GUI.Label(new Rect(x + 12, y + 24, ANCHO - 24, 34), _avisoDetalle, _detalleStyle);

        // Un clic encima lo descarta antes de tiempo.
        if (Event.current.type == EventType.MouseDown && caja.Contains(Event.current.mousePosition))
        {
            _aviso = Aviso.Ninguno;
            Event.current.Use();
        }
    }

    private void PrepararEstilos()
    {
        if (_estilosListos) return;

        _bgCaja = Tex(new Color(0.06f, 0.06f, 0.15f, 0.88f));
        _bgOk = Tex(new Color(0.07f, 0.22f, 0.09f, 0.92f));
        _bgError = Tex(new Color(0.28f, 0.07f, 0.07f, 0.92f));

        _cajaStyle = new GUIStyle(GUI.skin.box);
        _cajaStyle.normal.background = _bgCaja;

        _cajaOkStyle = new GUIStyle(GUI.skin.box);
        _cajaOkStyle.normal.background = _bgOk;

        _cajaErrorStyle = new GUIStyle(GUI.skin.box);
        _cajaErrorStyle.normal.background = _bgError;

        _tituloStyle = new GUIStyle(GUI.skin.label);
        _tituloStyle.fontSize = 13;
        _tituloStyle.fontStyle = FontStyle.Bold;
        _tituloStyle.normal.textColor = Color.white;

        _detalleStyle = new GUIStyle(GUI.skin.label);
        _detalleStyle.fontSize = 11;
        _detalleStyle.wordWrap = true;
        _detalleStyle.normal.textColor = new Color(0.78f, 0.84f, 0.94f);

        _estilosListos = true;
    }

    private static string Recortar(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\n", " ");
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }

    private static Texture2D Tex(Color c)
    {
        Texture2D t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
