using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class Recorder : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Tecla para iniciar/parar la grabacion (por defecto: R)")]
    public KeyCode recordToggleKey = KeyCode.R;
    public float snapshotTime = 0.1f;
    public string csvOutputName = "SoccerData";

    // Estado de grabacion
    private bool isRecording  = false;
    private int  totalFrames  = 0;      // snapshots acumulados en esta sesion
    private int  totalSegments = 0;     // veces que se ha activado la grabacion

    [Header("References")]
    public PlayerID myPlayer;
    public Rigidbody myRigidbody;
    public CharacterGV myCharacterGV;

    [Header("Goals")]
    [Tooltip("Si se deja vacío el Recorder busca las Porterías automáticamente por componente Porteria")]
    public Transform rivalGoalTransform;
    public Transform ownGoalTransform;

    private const string IC = ""; // se usa InvariantCulture en toda la serialización

    private float timeElapsed = 0f;
    private float totalTime   = 0f;
    private List<string> recordedLines = new List<string>();

    private static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    // ---- GUI ---------------------------------------------------------------
    private GUIStyle _guiStyle;
    private void OnGUI()
    {
        if (_guiStyle == null)
        {
            _guiStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 18,
                alignment = TextAnchor.MiddleLeft
            };
            _guiStyle.normal.textColor = Color.white;
        }

        string status = isRecording
            ? $"<color=red>● REC</color>   {totalFrames} frames  |  segmento {totalSegments}"
            : $"<color=grey>■ PARADO</color>   {totalFrames} frames totales  |  {totalSegments} segmento(s)";

        GUI.Box(new Rect(10, 10, 370, 34), status, _guiStyle);
        GUI.Label(new Rect(14, 48, 370, 22),
            $"Pulsa [{recordToggleKey}] para {(isRecording ? "parar" : "iniciar")} la grabacion");
    }

    private void Start()
    {
        if (myPlayer     == null) myPlayer      = GetComponent<PlayerID>();
        if (myRigidbody  == null) myRigidbody   = GetComponent<Rigidbody>();
        if (myCharacterGV == null) myCharacterGV = GetComponent<CharacterGV>();

        // Buscar porterías automáticamente si no se asignaron en el inspector
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

            Debug.Log($"[Recorder] Porterías encontradas automáticamente " +
                      $"| Propia: {(ownGoalTransform != null ? ownGoalTransform.name : "NO ENCONTRADA")} " +
                      $"| Rival: {(rivalGoalTransform != null ? rivalGoalTransform.name : "NO ENCONTRADA")}");
        }

        // ---- CABECERA CSV ------------------------------------------------
        string header =
            // --- Estado propio ---
            "PosX,PosZ," +
            "OrientacionX,OrientacionZ," +
            "TienePelota," +
            // --- Pelota ---
            "PosPelotaX,PosPelotaZ," +
            "DistPelota," +
            "TienePelotaEquipo," +
            // --- Porterías y marcador ---
            "DistPorteriaContraria," +
            "DistPorteriaPropia," +
            "PuntuacionPropia,PuntuacionContraria," +
            "DistPelotaPorteriaPropia," +
            // --- Distancia al más cercano (resumen) ---
            "DistAliadoCercano," +
            "DistEnemigoCercano," +
            // --- 3 aliados más cercanos: posición + dirección ---
            "Aliado1PosX,Aliado1PosZ,Aliado1DirX,Aliado1DirZ," +
            "Aliado2PosX,Aliado2PosZ,Aliado2DirX,Aliado2DirZ," +
            "Aliado3PosX,Aliado3PosZ,Aliado3DirX,Aliado3DirZ," +
            // --- 3 enemigos más cercanos: posición + dirección ---
            "Enemigo1PosX,Enemigo1PosZ,Enemigo1DirX,Enemigo1DirZ," +
            "Enemigo2PosX,Enemigo2PosZ,Enemigo2DirX,Enemigo2DirZ," +
            "Enemigo3PosX,Enemigo3PosZ,Enemigo3DirX,Enemigo3DirZ," +
            // --- LABELS ---
            "InputX,InputZ," +
            "Disparo,Pase";

        recordedLines.Add(header);
    }

    private void Update()
    {
        // --- Toggle de grabación ---
        if (Input.GetKeyDown(recordToggleKey))
        {
            isRecording = !isRecording;
            if (isRecording)
            {
                totalSegments++;
                Debug.Log($"[Recorder] Grabación INICIADA (segmento {totalSegments})");
            }
            else
            {
                Debug.Log($"[Recorder] Grabación PAUSADA — {totalFrames} frames acumulados en {totalSegments} segmento(s)");
            }
        }

        if (!isRecording) return;

        totalTime   += Time.deltaTime;
        timeElapsed += Time.deltaTime;

        if (timeElapsed >= snapshotTime)
        {
            timeElapsed -= snapshotTime;
            RecordSnapshot();
            totalFrames++;
        }
    }

    // ------------------------------------------------------------------ //
    private void RecordSnapshot()
    {
        Vector3 myPos = transform.position;

        // ---- Pelota -------------------------------------------------------
        Vector3 ballPos   = Bola.instance != null ? Bola.instance.transform.position : Vector3.zero;
        float distToBall  = Vector3.Distance(myPos, ballPos);

        // ---- ¿Quién tiene la pelota? --------------------------------------
        int hasBallTeam  = 0;
        int myHasBall    = 0;
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

        // ---- Porterías ----------------------------------------------------
        float distToRivalGoal = rivalGoalTransform != null
            ? Vector3.Distance(myPos, rivalGoalTransform.position) : 0f;
        float distToOwnGoal   = ownGoalTransform != null
            ? Vector3.Distance(myPos, ownGoalTransform.position) : 0f;

        float distBallToOwnGoal = ownGoalTransform != null
            ? Vector3.Distance(ballPos, ownGoalTransform.position) : 0f;

        // ---- Puntuaciones -------------------------------------------------
        int scoreT1 = 0; // TODO: conectar al Arbitro cuando esté disponible
        int scoreT2 = 0;
        int puntuacionPropia = myPlayer.id % 2 == 0 ? scoreT1 : scoreT2;
        int puntuacionContraria = myPlayer.id % 2 == 0 ? scoreT2 : scoreT1;

        // ---- Hacia dónde mira el jugador controlado -----------------------
        Vector3 facing  = transform.forward;   // ya normalizado

        // ---- Clasificar todos los jugadores en aliados / rivales ----------
        PlayerID[] allPlayers = FindObjectsByType<PlayerID>(FindObjectsSortMode.None);

        var allies  = new List<(float dist, PlayerID p)>();
        var enemies = new List<(float dist, PlayerID p)>();

        foreach (PlayerID p in allPlayers)
        {
            // Excluir al propio jugador comparando el GameObject (más robusto que comparar el componente)
            if (p.gameObject == myPlayer.gameObject) continue;
            // Excluir porteros u objetos que no sean jugadores de campo si los tienes con PlayerID
            float d = Vector3.Distance(myPos, p.transform.position);
            if (p.id % 2 == myPlayer.id % 2)
                allies.Add((d, p));
            else
                enemies.Add((d, p));
        }

        allies.Sort((a, b) => a.dist.CompareTo(b.dist));
        enemies.Sort((a, b) => a.dist.CompareTo(b.dist));

        float distClosestAlly  = allies.Count  > 0 ? allies[0].dist  : 999f;
        float distClosestEnemy = enemies.Count > 0 ? enemies[0].dist : 999f;

        // ---- Helper: obtener información de un jugador (o 0,0 si no existe) -
        (float px, float pz, float dx, float dz) GetPlayerData(List<(float, PlayerID)> list, int index)
        {
            if (index >= list.Count) return (0f, 0f, 0f, 0f);
            PlayerID p  = list[index].Item2;
            Vector3 pos = p.transform.position;
            Rigidbody rb = p.GetComponent<Rigidbody>();
            Vector3 vel  = rb != null ? rb.linearVelocity : Vector3.zero;
            Vector3 dir  = vel.magnitude > 0.01f ? vel.normalized : Vector3.zero;
            return (pos.x, pos.z, dir.x, dir.z);
        }

        // 3 aliados más cercanos
        var (a1px, a1pz, a1dx, a1dz) = GetPlayerData(allies, 0);
        var (a2px, a2pz, a2dx, a2dz) = GetPlayerData(allies, 1);
        var (a3px, a3pz, a3dx, a3dz) = GetPlayerData(allies, 2);

        // 3 enemigos más cercanos
        var (e1px, e1pz, e1dx, e1dz) = GetPlayerData(enemies, 0);
        var (e2px, e2pz, e2dx, e2dz) = GetPlayerData(enemies, 1);
        var (e3px, e3pz, e3dx, e3dz) = GetPlayerData(enemies, 2);

        // ---- Labels -------------------------------------------------------
        float inputX     = Input.GetAxisRaw("Horizontal");
        float inputZ     = Input.GetAxisRaw("Vertical");
        int disparo = Input.GetKey(KeyCode.O) ? 1 : 0;   // O = Disparo a portería
        int pase    = Input.GetKey(KeyCode.P) ? 1 : 0;   // P = Pase

        // ---- Serializar fila CSV ------------------------------------------
        string F(float v) => v.ToString("F3", Inv);

        string row =
            $"{F(myPos.x)},{F(myPos.z)}," +
            $"{F(facing.x)},{F(facing.z)}," +
            $"{myHasBall}," +
            $"{F(ballPos.x)},{F(ballPos.z)}," +
            $"{F(distToBall)}," +
            $"{hasBallTeam}," +
            $"{F(distToRivalGoal)}," +
            $"{F(distToOwnGoal)}," +
            $"{puntuacionPropia},{puntuacionContraria}," +
            $"{F(distBallToOwnGoal)}," +
            $"{F(distClosestAlly)}," +
            $"{F(distClosestEnemy)}," +
            // aliados
            $"{F(a1px)},{F(a1pz)},{F(a1dx)},{F(a1dz)}," +
            $"{F(a2px)},{F(a2pz)},{F(a2dx)},{F(a2dz)}," +
            $"{F(a3px)},{F(a3pz)},{F(a3dx)},{F(a3dz)}," +
            // enemigos
            $"{F(e1px)},{F(e1pz)},{F(e1dx)},{F(e1dz)}," +
            $"{F(e2px)},{F(e2pz)},{F(e2dx)},{F(e2dz)}," +
            $"{F(e3px)},{F(e3pz)},{F(e3dx)},{F(e3dz)}," +
            // labels
            $"{F(inputX)},{F(inputZ)}," +
            $"{disparo},{pase}";

        recordedLines.Add(row);
    }

    public void SaveToFile()
    {
        if (!recordMode || recordedLines.Count <= 1) return;
        string date      = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
        string finalPath = Path.Combine(Application.dataPath, $"{csvOutputName}_{date}.csv");
        File.WriteAllLines(finalPath, recordedLines);
        Debug.Log($"[Recorder] Dataset guardado en: {finalPath}");
    }

    private void OnApplicationQuit() => SaveToFile();
}
