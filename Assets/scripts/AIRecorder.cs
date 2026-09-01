using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Graba lo que hace el jugador controlado por el modelo durante el partido,
/// para poder comparar su comportamiento con el del humano que lo entreno
/// (mapas de calor, ritmo de acciones, zonas de disparo...).
///
/// Escribe EL MISMO sistema de coordenadas que Recorder.cs: ademas de la
/// posicion absoluta guarda la posicion relativa a las dos porterias, que es
/// lo unico que graba el Recorder humano. Asi el script de heatmaps puede
/// tratar los dos ficheros con el mismo codigo, sin suponer que ambos equipos
/// atacan hacia el mismo lado.
///
/// Uso: va en el mismo GameObject que AIControllerFNNClasi.
/// CharacterManagerInField lo habilita solo en el jugador que lleva el modelo.
/// </summary>
public class AIRecorder : MonoBehaviour
{
    [Header("Configuracion")]
    [Tooltip("Intervalo entre snapshots en segundos (0.1 = 10 Hz, igual que Recorder.cs)")]
    public float snapshotInterval = 0.1f;

    [Tooltip("Nombre base del fichero de salida")]
    public string csvOutputName = "AIData";

    [Tooltip("Si es false no graba nada, sin necesidad de quitar el componente")]
    public bool recordingEnabled = true;

    [Tooltip("No graba los frames del saque tras gol: el jugador esta congelado " +
             "en el punto de spawn y esos frames crean un pico falso en el mapa.")]
    public bool ignorarFramesDeReset = true;

    [Header("Referencias (se autodetectan si se dejan vacias)")]
    public PlayerID myPlayer;
    public Transform rivalGoalTransform;
    public Transform ownGoalTransform;

    // ---- Internos ----
    private float _timer;
    private int _totalFrames;
    private readonly List<string> _lines = new List<string>();
    private bool _guardado;

    // Acciones ejecutadas desde el ultimo snapshot. El controlador las avisa
    // con RegistrarAccion(); aqui solo se vuelcan. Antes estas columnas se
    // escribian siempre a 0, asi que no habia forma de dibujar donde dispara
    // o roba el modelo.
    private readonly bool[] _accionPendiente = new bool[4];
    public const int ACCION_DISPARO = 0;
    public const int ACCION_PASE = 1;
    public const int ACCION_ROBO_K = 2;
    public const int ACCION_ROBO_L = 3;

    private static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    private const string CABECERA =
        "AbsMyPosX,AbsMyPosZ,AbsBallPosX,AbsBallPosZ," +
        "RelPorteriaRivalX,RelPorteriaRivalZ," +
        "RelPorteriaPropiaX,RelPorteriaPropiaZ," +
        "RelPelotaX,RelPelotaZ,DistPelota," +
        "TienePelota,TienePelotaEquipo," +
        "InputX,InputZ," +
        "Disparo,Pase,RoboK,RoboL";

    // ========================================================================
    // CICLO DE VIDA
    // ========================================================================
    private void Start()
    {
        if (!recordingEnabled) return;

        if (myPlayer == null) myPlayer = GetComponent<PlayerID>();

        BuscarPorterias();

        _lines.Add(CABECERA);
        Debug.Log("[AIRecorder] Grabando en " + gameObject.name +
                  " (modelo '" + NombreDelModelo() + "')");
    }

    private void Update()
    {
        if (!recordingEnabled) return;

        _timer += Time.deltaTime;
        if (_timer < snapshotInterval) return;
        _timer -= snapshotInterval;

        if (ignorarFramesDeReset && EnReset())
        {
            LimpiarAcciones();
            return;
        }

        Capturar();
        _totalFrames++;
    }

    // Se llama al descargar la escena. Es la ruta importante: el partido
    // termina en Timer.EndMatch() con un LoadScene, que destruye este objeto
    // ANTES de que Unity llegue a OnApplicationQuit. Con el guardado solo en
    // OnApplicationQuit la grabacion se perdia entera en toda partida que
    // acabase por tiempo, que son todas.
    private void OnDestroy() { Guardar(); }

    private void OnApplicationQuit() { Guardar(); }

    // ========================================================================
    // CAPTURA
    // ========================================================================
    private void Capturar()
    {
        Vector3 myPos = transform.position;
        Vector3 ballPos = Bola.instance != null
            ? Bola.instance.transform.position
            : Vector3.zero;

        Vector3 rivalGoal = rivalGoalTransform != null ? rivalGoalTransform.position : Vector3.zero;
        Vector3 ownGoal = ownGoalTransform != null ? ownGoalTransform.position : Vector3.zero;

        float relRivalX = rivalGoal.x - myPos.x;
        float relRivalZ = rivalGoal.z - myPos.z;
        float relOwnX = ownGoal.x - myPos.x;
        float relOwnZ = ownGoal.z - myPos.z;

        float relBallX = ballPos.x - myPos.x;
        float relBallZ = ballPos.z - myPos.z;
        float distBall = Vector3.Distance(myPos, ballPos);

        int tienePelota = TengoLaPelota() ? 1 : 0;
        int posesionEquipo = PosesionDelEquipo();

        // Direccion de movimiento discretizada a {-1,0,1}, como InputX/InputZ
        // del humano. El umbral es sobre velocidad del Rigidbody: las
        // velocidades del juego son de cientos de unidades, asi que cualquier
        // valor pequenyo separa bien "quieto" de "moviendose".
        float vx = 0f, vz = 0f;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            const float UMBRAL_VEL = 1f;
            vx = rb.linearVelocity.x;
            vz = rb.linearVelocity.z;
            vx = vx > UMBRAL_VEL ? 1f : (vx < -UMBRAL_VEL ? -1f : 0f);
            vz = vz > UMBRAL_VEL ? 1f : (vz < -UMBRAL_VEL ? -1f : 0f);
        }

        _lines.Add(
            F(myPos.x) + "," + F(myPos.z) + "," +
            F(ballPos.x) + "," + F(ballPos.z) + "," +
            F(relRivalX) + "," + F(relRivalZ) + "," +
            F(relOwnX) + "," + F(relOwnZ) + "," +
            F(relBallX) + "," + F(relBallZ) + "," + F(distBall) + "," +
            tienePelota + "," + posesionEquipo + "," +
            F(vx) + "," + F(vz) + "," +
            (_accionPendiente[0] ? 1 : 0) + "," +
            (_accionPendiente[1] ? 1 : 0) + "," +
            (_accionPendiente[2] ? 1 : 0) + "," +
            (_accionPendiente[3] ? 1 : 0));

        LimpiarAcciones();
    }

    /// <summary>
    /// El controlador avisa aqui cada vez que ejecuta una accion. Se queda
    /// marcada hasta el siguiente snapshot para que no se pierda ninguna:
    /// las acciones duran un frame y los snapshots van a 10 Hz.
    /// </summary>
    public void RegistrarAccion(int accion)
    {
        if (!recordingEnabled) return;
        if (accion < 0 || accion >= _accionPendiente.Length) return;
        _accionPendiente[accion] = true;
    }

    private void LimpiarAcciones()
    {
        for (int i = 0; i < _accionPendiente.Length; i++) _accionPendiente[i] = false;
    }

    // ========================================================================
    // GUARDADO
    // ========================================================================
    private void Guardar()
    {
        if (_guardado) return;                 // OnDestroy y OnApplicationQuit pueden dispararse los dos
        if (!recordingEnabled) return;
        if (_lines.Count <= 1) return;
        _guardado = true;

        string nombre = csvOutputName + "_" + Sanear(NombreDelModelo()) + "_" +
                        DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss") + ".csv";

        // persistentDataPath es el unico sitio en el que se puede escribir
        // desde una build. Application.dataPath en build es la carpeta
        // *_Data del juego, no la carpeta Assets del proyecto.
        string destino = Path.Combine(Application.persistentDataPath, nombre);
        try
        {
            File.WriteAllLines(destino, _lines);
            Debug.Log("[AIRecorder] " + _totalFrames + " snapshots -> " + destino);
        }
        catch (Exception e)
        {
            Debug.LogError("[AIRecorder] No se pudo guardar en " + destino + ": " + e.Message);
            return;
        }

#if UNITY_EDITOR
        // En el editor, ademas, una copia en Assets/ para tenerla junto a los
        // SoccerData_*.csv del humano.
        try { File.WriteAllLines(Path.Combine(Application.dataPath, nombre), _lines); }
        catch (Exception e) { Debug.LogWarning("[AIRecorder] copia en Assets fallida: " + e.Message); }
#endif
    }

    // ========================================================================
    // AYUDAS
    // ========================================================================
    /// <summary>
    /// Compara por GameObject, no por componente. En este objeto conviven
    /// varios PlayerID (el rol y el CharacterGV), asi que GetComponent puede
    /// devolver uno distinto del que Bola guardo como Owner y la comparacion
    /// por referencia daba siempre false.
    /// </summary>
    private bool TengoLaPelota()
    {
        if (Bola.instance == null || !Bola.instance.EnPosesion) return false;
        PlayerID owner = Bola.instance.Owner;
        return owner != null && owner.gameObject == gameObject;
    }

    /// <summary>0 = nadie, 1 = mi equipo, 2 = el rival. Mismo criterio que Recorder.cs.</summary>
    private int PosesionDelEquipo()
    {
        if (Bola.instance == null || !Bola.instance.EnPosesion) return 0;
        PlayerID owner = Bola.instance.Owner;
        if (owner == null || myPlayer == null) return 0;
        return (owner.id % 2 == myPlayer.id % 2) ? 1 : 2;
    }

    private bool EnReset()
    {
        PlayerID[] todos = GetComponents<PlayerID>();
        for (int i = 0; i < todos.Length; i++)
            if (todos[i] != null && todos[i].EnReset) return true;
        return false;
    }

    private void BuscarPorterias()
    {
        if (rivalGoalTransform != null && ownGoalTransform != null) return;

        Porteria[] porterias = FindObjectsByType<Porteria>(FindObjectsSortMode.None);
        int myTeam = myPlayer != null ? myPlayer.id % 2 : 0;

        foreach (Porteria p in porterias)
        {
            bool esPropia = (p.team % 2 == myTeam);
            if (esPropia && ownGoalTransform == null) ownGoalTransform = p.transform;
            else if (!esPropia && rivalGoalTransform == null) rivalGoalTransform = p.transform;
        }

        if (rivalGoalTransform == null || ownGoalTransform == null)
            Debug.LogWarning("[AIRecorder] No se encontraron las dos porterias: las columnas " +
                             "relativas saldran a cero y el mapa no se podra alinear con el humano.");
    }

    private string NombreDelModelo()
    {
        if (Data.instance != null && !string.IsNullOrEmpty(Data.instance.rutaModeloONNX))
        {
            string f = Path.GetFileNameWithoutExtension(Data.instance.rutaModeloONNX);
            const string PREFIJO = "SoccerModel_";
            if (f.StartsWith(PREFIJO, StringComparison.Ordinal))
                f = f.Substring(PREFIJO.Length);
            if (!string.IsNullOrEmpty(f)) return f;
        }
        return "desconocido";
    }

    private static string Sanear(string s)
    {
        char[] malos = Path.GetInvalidFileNameChars();
        foreach (char c in malos) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    private static string F(float v)
    {
        return v.ToString("F3", Inv);
    }
}
