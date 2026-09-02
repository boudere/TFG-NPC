using System.IO;
using UnityEngine;

/// <summary>
/// Marca en pantalla cual de los once jugadores lleva el modelo enchufado.
///
/// Dibuja un triangulo y el nombre del modelo justo encima de la cabeza del
/// agente, siguiendolo por el campo. Se pinta con IMGUI y se proyecta con
/// WorldToScreenPoint, asi que NO necesita prefabs, ni Canvas, ni tocar la
/// escena: el propio AIControllerFNNClasi se lo anyade al arrancar, y como
/// solo esta habilitado en el jugador del modelo, el indicador aparece
/// exactamente sobre el que toca.
///
/// F2 lo muestra u oculta, para poder grabar video limpio.
/// </summary>
public class IndicadorModelo : MonoBehaviour
{
    [Header("Aspecto")]
    [Tooltip("Altura sobre el jugador, en unidades de mundo")]
    public float alturaMarcador = 95f;

    [Tooltip("Naranja del modelo, el mismo que en los mapas de calor")]
    public Color color = new Color(0.92f, 0.41f, 0.20f, 1f);

    [Tooltip("Texto a mostrar en vez del nombre del modelo descargado. Lo usan\n" +
             "los controladores GRU y sliding window, que cargan su ONNX desde el\n" +
             "inspector y no pasan por Data.rutaModeloONNX.")]
    public string nombreOverride = "";

    [Tooltip("Tecla para mostrar u ocultar el indicador")]
    public KeyCode teclaVisibilidad = KeyCode.F2;

    [Tooltip("Ancho del triangulo en pixeles")]
    public float anchoTriangulo = 26f;
    public float altoTriangulo = 14f;

    private bool _visible = true;
    private string _nombreModelo = "";
    private Camera _camara;
    private GUIStyle _estilo;
    private Texture2D _pixel;

    // ------------------------------------------------------------------
    private void Start()
    {
        _nombreModelo = string.IsNullOrEmpty(nombreOverride)
                        ? LeerNombreModelo()
                        : nombreOverride;

        _pixel = new Texture2D(1, 1);
        _pixel.SetPixel(0, 0, Color.white);
        _pixel.Apply();
        _pixel.hideFlags = HideFlags.HideAndDontSave;
    }

    private void OnDestroy()
    {
        if (_pixel != null) Destroy(_pixel);
    }

    private void Update()
    {
        // Via InputLock para que la tecla no dispare mientras se escribe
        // el nombre del modelo en un cuadro de texto.
        if (InputLock.GetKeyDown(teclaVisibilidad))
            _visible = !_visible;
    }

    // ------------------------------------------------------------------
    private void OnGUI()
    {
        if (!_visible) return;

        Camera cam = CamaraActiva();
        if (cam == null) return;

        Vector3 mundo = transform.position + Vector3.up * alturaMarcador;
        Vector3 sp = cam.WorldToScreenPoint(mundo);

        // z < 0 significa que el punto queda detras de la camara: si no se
        // comprueba, Unity lo proyecta igualmente y el marcador aparece
        // duplicado en el lado contrario de la pantalla.
        if (sp.z <= 0f) return;

        float x = sp.x;
        float y = Screen.height - sp.y;      // IMGUI cuenta la Y desde arriba
        if (x < -100f || x > Screen.width + 100f || y < -100f || y > Screen.height + 100f)
            return;

        if (_estilo == null)
        {
            _estilo = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
        }

        // Latido lento: llama la atencion sin resultar molesto.
        float pulso = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f);
        Color c = color;

        string texto = string.IsNullOrEmpty(_nombreModelo)
            ? "MODELO"
            : "MODELO  " + _nombreModelo;

        Vector2 tam = _estilo.CalcSize(new GUIContent(texto));
        float anchoCaja = tam.x + 16f;
        float altoCaja = tam.y + 6f;
        float cajaY = y - altoTriangulo - altoCaja - 2f;

        // Fondo de la etiqueta
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(x - anchoCaja / 2f, cajaY, anchoCaja, altoCaja), _pixel);
        // Filo superior en el color del modelo
        GUI.color = c;
        GUI.DrawTexture(new Rect(x - anchoCaja / 2f, cajaY, anchoCaja, 2f), _pixel);

        // Texto
        GUI.color = Color.white;
        GUI.Label(new Rect(x - anchoCaja / 2f, cajaY + 3f, anchoCaja, tam.y), texto, _estilo);

        // Triangulo apuntando al jugador, dibujado por franjas horizontales
        // para no depender de ningun sprite.
        c.a = pulso;
        GUI.color = c;
        int franjas = Mathf.CeilToInt(altoTriangulo);
        for (int i = 0; i < franjas; i++)
        {
            float t = i / (float)franjas;                 // 0 arriba, 1 en la punta
            float ancho = Mathf.Lerp(anchoTriangulo, 1f, t);
            GUI.DrawTexture(new Rect(x - ancho / 2f, y - altoTriangulo + i, ancho, 1f), _pixel);
        }

        GUI.color = Color.white;
    }

    // ------------------------------------------------------------------
    private Camera CamaraActiva()
    {
        if (_camara != null && _camara.isActiveAndEnabled) return _camara;

        _camara = Camera.main;
        if (_camara != null && _camara.isActiveAndEnabled) return _camara;

        // La escena tiene varias camaras y la principal puede estar apagada
        // segun el modo, asi que se coge la primera que este activa.
        Camera[] todas = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera c in todas)
        {
            if (c != null && c.isActiveAndEnabled) { _camara = c; return _camara; }
        }
        return null;
    }

    private static string LeerNombreModelo()
    {
        if (Data.instance == null || string.IsNullOrEmpty(Data.instance.rutaModeloONNX))
            return "";

        string f = Path.GetFileNameWithoutExtension(Data.instance.rutaModeloONNX);
        const string PREFIJO = "SoccerModel_";
        if (f.StartsWith(PREFIJO, System.StringComparison.Ordinal))
            f = f.Substring(PREFIJO.Length);
        return f;
    }
}
