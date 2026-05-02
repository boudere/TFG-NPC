using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class Recorder : MonoBehaviour
{
    // ─── Inspector ──────────────────────────────────────────────────────────
    [Header("Configuration")]
    [Tooltip("Tecla para iniciar/parar la grabacion")]
    public KeyCode recordToggleKey = KeyCode.R;
    [Tooltip("Tecla para mostrar/ocultar el panel de estadísticas")]
    public KeyCode statsToggleKey  = KeyCode.M;
    public float  snapshotTime  = 0.1f;
    public string csvOutputName = "SoccerData";

    [Header("References")]
    public PlayerID    myPlayer;
    public Rigidbody   myRigidbody;
    public CharacterGV myCharacterGV;

    [Header("Goals")]
    [Tooltip("Si se deja vacío el Recorder busca las Porterías automáticamente")]
    public Transform rivalGoalTransform;
    public Transform ownGoalTransform;

    // ─── Estado de grabación ────────────────────────────────────────────────
    private bool         isRecording    = false;
    private int          totalFrames    = 0;
    private int          totalSegments  = 0;
    private float        timeElapsed    = 0f;
    private float        totalTime      = 0f;
    private List<string> recordedLines  = new List<string>();

    private static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    // ─── Estadísticas acumuladas ────────────────────────────────────────────
    private bool _showStats        = false;
    private int  _possessionFrames = 0;
    private int  _forwardFrames    = 0;
    private int  _backwardFrames   = 0;
    private int  _lateralFrames    = 0;
    private int  _stoppedFrames    = 0;
    private int  _ownZoneFrames    = 0;
    private int  _centerFrames     = 0;
    private int  _rivalZoneFrames  = 0;
    private int  _shotCount        = 0;
    private int  _passCount        = 0;

    // ─── Umbrales ───────────────────────────────────────────────────────────
    private const int   SHOT_OK    = 30;
    private const int   SHOT_WARN  = 15;
    private const int   PASS_OK    = 35;
    private const int   PASS_WARN  = 15;
    private const float POSS_OK    = 25f;
    private const float POSS_WARN  = 12f;
    private const float STOP_WARN  = 30f;
    private const float STOP_BAD   = 45f;
    private const float RIVAL_OK   = 30f;
    private const float RIVAL_WARN = 18f;

    // ─── GUI cache ───────────────────────────────────────────────────────────
    private GUIStyle  _statusStyle;
    private GUIStyle  _hintStyle;
    private GUIStyle  _panelStyle;
    private GUIStyle  _titleStyle;
    private GUIStyle  _sectionStyle;
    private GUIStyle  _rowStyle;
    private Texture2D _darkBg;
    private Texture2D _barBg;
    private Texture2D _barGreen;
    private Texture2D _barYellow;
    private Texture2D _barRed;
    private Texture2D _barBlue;

    // ========================================================================
    // INICIO
    // ========================================================================
    private void Start()
    {
        if (myPlayer      == null) myPlayer      = GetComponent<PlayerID>();
        if (myRigidbody   == null) myRigidbody   = GetComponent<Rigidbody>();
        if (myCharacterGV == null) myCharacterGV = GetComponent<CharacterGV>();

        if (rivalGoalTransform == null || ownGoalTransform == null)
        {
            Porteria[] porterias = FindObjectsByType<Porteria>(FindObjectsSortMode.None);
            int myTeam = myPlayer != null ? myPlayer.id % 2 : 0;
            foreach (Porteria p in porterias)
            {
                bool isOwn = (p.team % 2 == myTeam);
                if (isOwn  && ownGoalTransform   == null) ownGoalTransform   = p.transform;
                if (!isOwn && rivalGoalTransform  == null) rivalGoalTransform = p.transform;
            }
        }

        // Cabecera CSV (40 features + labels + posiciones absolutas)
        string header =
            "RelPorteriaRivalX,RelPorteriaRivalZ," +
            "RelPorteriaPropiaX,RelPorteriaPropiaZ," +
            "TienePelota," +
            "RelPelotaX,RelPelotaZ," +
            "DistPelota," +
            "TienePelotaEquipo," +
            "DistPorteriaContraria," +
            "DistPorteriaPropia," +
            "PuntuacionPropia,PuntuacionContraria," +
            "DistPelotaPorteriaPropia," +
            "DistAliadoCercano," +
            "DistEnemigoCercano," +
            "RelAliado1PosX,RelAliado1PosZ,Aliado1DirX,Aliado1DirZ," +
            "RelAliado2PosX,RelAliado2PosZ,Aliado2DirX,Aliado2DirZ," +
            "RelAliado3PosX,RelAliado3PosZ,Aliado3DirX,Aliado3DirZ," +
            "RelEnemigo1PosX,RelEnemigo1PosZ,Enemigo1DirX,Enemigo1DirZ," +
            "RelEnemigo2PosX,RelEnemigo2PosZ,Enemigo2DirX,Enemigo2DirZ," +
            "RelEnemigo3PosX,RelEnemigo3PosZ,Enemigo3DirX,Enemigo3DirZ," +
            "InputX,InputZ," +
            "Disparo,Pase," +
            "AbsMyPosX,AbsMyPosZ," +
            "AbsBallPosX,AbsBallPosZ";

        recordedLines.Add(header);
    }

    // ========================================================================
    // UPDATE
    // ========================================================================
    private void Update()
    {
        // Toggle grabación
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
                Debug.Log($"[Recorder] Grabación PAUSADA — {totalFrames} frames acumulados");
            }
        }

        // Toggle panel de estadísticas
        if (Input.GetKeyDown(statsToggleKey))
            _showStats = !_showStats;

        if (!isRecording) return;

        totalTime   += Time.deltaTime;
        timeElapsed += Time.deltaTime;

        bool iHaveBall = Bola.instance != null && Bola.instance.EnPosesion &&
                         Bola.instance.Owner != null &&
                         Bola.instance.Owner.GetComponent<PlayerID>() == myPlayer;

        bool forcePase = Input.GetKeyDown(KeyCode.P) && iHaveBall;
        bool forceTiro = Input.GetKeyDown(KeyCode.O) && iHaveBall;

        if (forcePase || forceTiro)
        {
            RecordSnapshot(forcePase, forceTiro);
            totalFrames++;
            timeElapsed = 0f;
        }
        else if (timeElapsed >= snapshotTime)
        {
            timeElapsed -= snapshotTime;
            RecordSnapshot();
            totalFrames++;
        }
    }

    // ========================================================================
    // SNAPSHOT
    // ========================================================================
    private void RecordSnapshot(bool forcePase = false, bool forceTiro = false)
    {
        Vector3 myPos = transform.position;

        Vector3 rivalGoalPos = rivalGoalTransform != null ? rivalGoalTransform.position : Vector3.zero;
        Vector3 ownGoalPos   = ownGoalTransform   != null ? ownGoalTransform.position   : Vector3.zero;

        float relRivalGoalX = rivalGoalPos.x - myPos.x;
        float relRivalGoalZ = rivalGoalPos.z - myPos.z;
        float relOwnGoalX   = ownGoalPos.x - myPos.x;
        float relOwnGoalZ   = ownGoalPos.z - myPos.z;

        Vector3 ballPos  = Bola.instance != null ? Bola.instance.transform.position : Vector3.zero;
        float relBallX   = ballPos.x - myPos.x;
        float relBallZ   = ballPos.z - myPos.z;
        float distToBall = Vector3.Distance(myPos, ballPos);

        int hasBallTeam = 0, myHasBall = 0;
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

        float distToRivalGoal    = Vector3.Distance(myPos, rivalGoalPos);
        float distToOwnGoal      = Vector3.Distance(myPos, ownGoalPos);
        float distBallToOwnGoal  = Vector3.Distance(ballPos, ownGoalPos);

        int scoreT1 = 0, scoreT2 = 0;
        int puntuacionPropia    = myPlayer.id % 2 == 0 ? scoreT1 : scoreT2;
        int puntuacionContraria = myPlayer.id % 2 == 0 ? scoreT2 : scoreT1;

        PlayerID[] allPlayers = FindObjectsByType<PlayerID>(FindObjectsSortMode.None);
        var allies  = new List<(float, PlayerID)>();
        var enemies = new List<(float, PlayerID)>();

        foreach (PlayerID p in allPlayers)
        {
            if (p.gameObject == myPlayer.gameObject) continue;
            float d = Vector3.Distance(myPos, p.transform.position);
            if (p.id % 2 == myPlayer.id % 2) allies.Add((d, p));
            else                              enemies.Add((d, p));
        }
        allies.Sort((a, b)  => a.Item1.CompareTo(b.Item1));
        enemies.Sort((a, b) => a.Item1.CompareTo(b.Item1));

        float distClosestAlly  = allies.Count  > 0 ? allies[0].Item1  : 999f;
        float distClosestEnemy = enemies.Count > 0 ? enemies[0].Item1 : 999f;

        (float rx, float rz, float dx, float dz) GetPlayerData(List<(float, PlayerID)> list, int i)
        {
            if (i >= list.Count) return (0f, 0f, 0f, 0f);
            PlayerID p = list[i].Item2;
            Vector3 pos = p.transform.position;
            Rigidbody rb = p.GetComponent<Rigidbody>();
            Vector3 vel  = rb != null ? rb.linearVelocity : Vector3.zero;
            Vector3 dir  = vel.magnitude > 0.01f ? vel.normalized : Vector3.zero;
            return (pos.x - myPos.x, pos.z - myPos.z, dir.x, dir.z);
        }

        var (a1px,a1pz,a1dx,a1dz) = GetPlayerData(allies,  0);
        var (a2px,a2pz,a2dx,a2dz) = GetPlayerData(allies,  1);
        var (a3px,a3pz,a3dx,a3dz) = GetPlayerData(allies,  2);
        var (e1px,e1pz,e1dx,e1dz) = GetPlayerData(enemies, 0);
        var (e2px,e2pz,e2dx,e2dz) = GetPlayerData(enemies, 1);
        var (e3px,e3pz,e3dx,e3dz) = GetPlayerData(enemies, 2);

        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");
        int disparo  = (forceTiro || Input.GetKey(KeyCode.O)) ? 1 : 0;
        int pase     = (forcePase || Input.GetKey(KeyCode.P)) ? 1 : 0;

        string F(float v) => v.ToString("F3", Inv);

        string row =
            $"{F(relRivalGoalX)},{F(relRivalGoalZ)}," +
            $"{F(relOwnGoalX)},{F(relOwnGoalZ)}," +
            $"{myHasBall}," +
            $"{F(relBallX)},{F(relBallZ)}," +
            $"{F(distToBall)}," +
            $"{hasBallTeam}," +
            $"{F(distToRivalGoal)}," +
            $"{F(distToOwnGoal)}," +
            $"{puntuacionPropia},{puntuacionContraria}," +
            $"{F(distBallToOwnGoal)}," +
            $"{F(distClosestAlly)}," +
            $"{F(distClosestEnemy)}," +
            $"{F(a1px)},{F(a1pz)},{F(a1dx)},{F(a1dz)}," +
            $"{F(a2px)},{F(a2pz)},{F(a2dx)},{F(a2dz)}," +
            $"{F(a3px)},{F(a3pz)},{F(a3dx)},{F(a3dz)}," +
            $"{F(e1px)},{F(e1pz)},{F(e1dx)},{F(e1dz)}," +
            $"{F(e2px)},{F(e2pz)},{F(e2dx)},{F(e2dz)}," +
            $"{F(e3px)},{F(e3pz)},{F(e3dx)},{F(e3dz)}," +
            $"{F(inputX)},{F(inputZ)}," +
            $"{disparo},{pase}," +
            $"{F(myPos.x)},{F(myPos.z)}," +
            $"{F(ballPos.x)},{F(ballPos.z)}";

        recordedLines.Add(row);

        // ── Actualizar estadísticas ──────────────────────────────────────────
        if (myHasBall == 1) _possessionFrames++;

        if      (inputZ >  0.3f)                          _forwardFrames++;
        else if (inputZ < -0.3f)                          _backwardFrames++;
        else if (Mathf.Abs(inputX) > 0.3f)               _lateralFrames++;
        else                                               _stoppedFrames++;

        if (disparo == 1) _shotCount++;
        if (pase    == 1) _passCount++;

        // Zona: comparar distancias a cada portería
        if (rivalGoalTransform != null && ownGoalTransform != null)
        {
            float midX        = (ownGoalPos.x + rivalGoalPos.x) * 0.5f;
            float fieldLen    = Mathf.Abs(rivalGoalPos.x - ownGoalPos.x);
            float centerRange = fieldLen * 0.15f;
            bool  rivalRight  = rivalGoalPos.x > ownGoalPos.x;

            float px = myPos.x;
            bool inOwn   = rivalRight ? px < midX - centerRange : px > midX + centerRange;
            bool inRival = rivalRight ? px > midX + centerRange : px < midX - centerRange;

            if      (inOwn)   _ownZoneFrames++;
            else if (inRival) _rivalZoneFrames++;
            else              _centerFrames++;
        }
    }

    // ========================================================================
    // GUI
    // ========================================================================
    private void InitStyles()
    {
        if (_statusStyle != null) return;

        // Barra de estado superior
        _statusStyle = new GUIStyle(GUI.skin.box)
            { fontSize = 18, alignment = TextAnchor.MiddleLeft, richText = true };
        _statusStyle.normal.textColor = Color.white;

        _hintStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 13, richText = true };
        _hintStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

        // Panel de estadísticas
        _darkBg = MakeTex(new Color(0.04f, 0.04f, 0.12f, 0.93f));
        _panelStyle = new GUIStyle(GUI.skin.box);
        _panelStyle.normal.background = _darkBg;

        _titleStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
        _titleStyle.normal.textColor = Color.white;

        _sectionStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 13, fontStyle = FontStyle.Bold, richText = true };
        _sectionStyle.normal.textColor = new Color(0.7f, 0.85f, 1f);

        _rowStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 13, richText = true };
        _rowStyle.normal.textColor = Color.white;

        // Barras de progreso
        _barBg     = MakeTex(new Color(0.15f, 0.15f, 0.15f, 0.9f));
        _barGreen  = MakeTex(new Color(0.20f, 0.78f, 0.35f, 1f));
        _barYellow = MakeTex(new Color(1.00f, 0.78f, 0.00f, 1f));
        _barRed    = MakeTex(new Color(0.88f, 0.18f, 0.12f, 1f));
        _barBlue   = MakeTex(new Color(0.25f, 0.55f, 0.95f, 1f));
    }

    private static Texture2D MakeTex(Color col)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, col);
        t.Apply();
        return t;
    }

    private void OnGUI()
    {
        InitStyles();

        // ── Barra de estado ─────────────────────────────────────────────────
        string recColor  = isRecording ? "red" : "grey";
        string recSymbol = isRecording ? "● REC" : "■ PARADO";
        string status    = $"<color={recColor}>{recSymbol}</color>   " +
                           $"{totalFrames} frames  |  segmento {totalSegments}";
        GUI.Box(new Rect(10, 10, 370, 34), status, _statusStyle);

        string hint = $"[{recordToggleKey}] grabar   [M] {(_showStats ? "ocultar" : "ver")} stats";
        GUI.Label(new Rect(14, 47, 370, 22), hint, _hintStyle);

        if (!_showStats) return;

        // ── Panel de estadísticas ────────────────────────────────────────────
        const int W   = 310;
        const int PAD = 8;
        int px = Screen.width - W - PAD;
        int py = 10;
        int lineH   = 22;
        int barH    = 12;
        int panelH  = 420;

        GUI.Box(new Rect(px - PAD, py, W + PAD * 2, panelH), "", _panelStyle);

        int cy = py + 8;

        // Título
        GUI.Label(new Rect(px, cy, W, 24), "📊  ESTADÍSTICAS DE ENTRENAMIENTO", _titleStyle);
        cy += 26;
        DrawHLine(px, cy, W); cy += 6;

        int n = Mathf.Max(totalFrames, 1);

        // ── ACCIONES ────────────────────────────────────────────────────────
        GUI.Label(new Rect(px, cy, W, lineH), "🎯  ACCIONES", _sectionStyle); cy += lineH;

        DrawActionRow(px, ref cy, W, barH, "Tiros",
            _shotCount, SHOT_OK,
            _shotCount >= SHOT_OK   ? _barGreen :
            _shotCount >= SHOT_WARN ? _barYellow : _barRed,
            _shotCount >= SHOT_OK   ? "✓ Bien" :
            _shotCount >= SHOT_WARN ? "⚠ Sube un poco" : "✗ Necesitas más tiros");

        DrawActionRow(px, ref cy, W, barH, "Pases",
            _passCount, PASS_OK,
            _passCount >= PASS_OK   ? _barGreen :
            _passCount >= PASS_WARN ? _barYellow : _barRed,
            _passCount >= PASS_OK   ? "✓ Bien" :
            _passCount >= PASS_WARN ? "⚠ Sube un poco" : "✗ Necesitas más pases");

        cy += 4; DrawHLine(px, cy, W); cy += 6;

        // ── POSESIÓN ────────────────────────────────────────────────────────
        float possPct = 100f * _possessionFrames / n;
        GUI.Label(new Rect(px, cy, W, lineH), "⚽  POSESIÓN DE BALÓN", _sectionStyle); cy += lineH;

        Texture2D possTex  = possPct >= POSS_OK ? _barGreen : possPct >= POSS_WARN ? _barYellow : _barRed;
        string    possHint = possPct >= POSS_OK ? "✓ Buena posesión" : possPct >= POSS_WARN ? "⚠ Un poco justa" : "✗ Busca más el balón";
        DrawPctRow(px, ref cy, W, barH, "Con balón", possPct, possTex, possHint);

        cy += 4; DrawHLine(px, cy, W); cy += 6;

        // ── MOVIMIENTO ──────────────────────────────────────────────────────
        GUI.Label(new Rect(px, cy, W, lineH), "🏃  MOVIMIENTO", _sectionStyle); cy += lineH;

        float fwdPct  = 100f * _forwardFrames  / n;
        float bwdPct  = 100f * _backwardFrames / n;
        float latPct  = 100f * _lateralFrames  / n;
        float stpPct  = 100f * _stoppedFrames  / n;

        DrawPctRow(px, ref cy, W, barH, "▲ Adelante", fwdPct,  _barBlue,   "");
        DrawPctRow(px, ref cy, W, barH, "▼ Atrás",    bwdPct,  _barBlue,   "");
        DrawPctRow(px, ref cy, W, barH, "◄► Lateral",  latPct,  _barBlue,   "");

        Texture2D stpTex  = stpPct < STOP_WARN ? _barGreen : stpPct < STOP_BAD ? _barYellow : _barRed;
        string    stpHint = stpPct < STOP_WARN ? "" : stpPct < STOP_BAD ? "⚠ Mucho tiempo parado" : "✗ Demasiado parado";
        DrawPctRow(px, ref cy, W, barH, "■ Parado",   stpPct,  stpTex,     stpHint);

        cy += 4; DrawHLine(px, cy, W); cy += 6;

        // ── ZONAS ───────────────────────────────────────────────────────────
        GUI.Label(new Rect(px, cy, W, lineH), "📍  ZONAS DEL CAMPO", _sectionStyle); cy += lineH;

        float ownPct = 100f * _ownZoneFrames   / n;
        float ctrPct = 100f * _centerFrames    / n;
        float rvlPct = 100f * _rivalZoneFrames / n;

        DrawPctRow(px, ref cy, W, barH, "Zona propia", ownPct, _barBlue, "");
        DrawPctRow(px, ref cy, W, barH, "Centro",      ctrPct, _barBlue, "");

        Texture2D rvlTex  = rvlPct >= RIVAL_OK ? _barGreen : rvlPct >= RIVAL_WARN ? _barYellow : _barRed;
        string    rvlHint = rvlPct >= RIVAL_OK ? "✓ Buena presión" : rvlPct >= RIVAL_WARN ? "⚠ Ataca más" : "✗ Muy defensivo";
        DrawPctRow(px, ref cy, W, barH, "Zona rival",  rvlPct, rvlTex,  rvlHint);
    }

    // ── Helpers GUI ──────────────────────────────────────────────────────────
    private void DrawHLine(int x, int y, int w)
    {
        var oldColor = GUI.color;
        GUI.color = new Color(0.35f, 0.45f, 0.65f, 0.7f);
        GUI.DrawTexture(new Rect(x, y, w, 1), Texture2D.whiteTexture);
        GUI.color = oldColor;
    }

    private void DrawActionRow(int x, ref int cy, int w, int barH,
        string label, int count, int target, Texture2D barTex, string hint)
    {
        // Texto principal
        string countColor = barTex == _barGreen ? "#55DD66" : barTex == _barYellow ? "#FFCC00" : "#FF5544";
        string line = $"{label,-10}  <color={countColor}><b>{count}</b></color>  / {target}   <color=#AAAAAA>{hint}</color>";
        GUI.Label(new Rect(x, cy, w, 20), line, _rowStyle);
        cy += 20;

        // Barra
        int  barW   = w - 4;
        int  fillW  = Mathf.RoundToInt(barW * Mathf.Clamp01((float)count / target));
        DrawBar(x + 2, cy, barW, barH, fillW, barTex);
        cy += barH + 4;
    }

    private void DrawPctRow(int x, ref int cy, int w, int barH,
        string label, float pct, Texture2D barTex, string hint)
    {
        string hintStr = hint.Length > 0 ? $"   <color=#AAAAAA>{hint}</color>" : "";
        string line    = $"{label,-12}  <b>{pct:F1}%</b>{hintStr}";
        GUI.Label(new Rect(x, cy, w, 20), line, _rowStyle);
        cy += 20;

        int barW  = w - 4;
        int fillW = Mathf.RoundToInt(barW * Mathf.Clamp01(pct / 100f));
        DrawBar(x + 2, cy, barW, barH, fillW, barTex);
        cy += barH + 4;
    }

    private void DrawBar(int x, int y, int totalW, int h, int fillW, Texture2D fillTex)
    {
        var bgStyle   = new GUIStyle { normal = { background = _barBg } };
        var fillStyle = new GUIStyle { normal = { background = fillTex } };
        GUI.Box(new Rect(x, y, totalW, h), GUIContent.none, bgStyle);
        if (fillW > 0)
            GUI.Box(new Rect(x, y, fillW, h), GUIContent.none, fillStyle);
    }

    // ========================================================================
    // GUARDADO
    // ========================================================================
    public void SaveToFile()
    {
        if (recordedLines.Count <= 1) return;
        string date      = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
        string finalPath = Path.Combine(Application.dataPath, $"{csvOutputName}_{date}.csv");
        File.WriteAllLines(finalPath, recordedLines);
        Debug.Log($"[Recorder] Dataset guardado en: {finalPath}  ({totalFrames} frames)");
    }

    private void OnApplicationQuit() => SaveToFile();
}
