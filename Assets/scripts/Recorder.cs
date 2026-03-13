using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class Recorder : MonoBehaviour
{
    [Header("Configuration")]
    public bool recordMode = true;
    public float snapshotTime = 0.1f; // Take a snapshot every 0.1 seconds
    public string csvOutputName = "SoccerData";

    [Header("References")]
    public PlayerID myPlayer; // The player this recorder is attached to
    public Rigidbody myRigidbody;
    public CharacterGV myCharacterGV;

    private float timeElapsed = 0f;
    private float totalTime = 0f;

    // To store data dynamically before saving
    private List<string> recordedLines = new List<string>();

    private void Start()
    {
        if (myPlayer == null) myPlayer = GetComponent<PlayerID>();
        if (myRigidbody == null) myRigidbody = GetComponent<Rigidbody>();
        if (myCharacterGV == null) myCharacterGV = GetComponent<CharacterGV>();

        // Agregamos la cabecera del CSV
        string header = "TotalTime," +
                        "MyPosX,MyPosZ," +
                        "BallPosX,BallPosZ," +
                        "DistToBall," +
                        "HasBallTeam," + // 0: libre, 1: team1, 2: team2 (sacado del árbitro o Bola)
                        "DistToRivalGoal," + // Aproximación
                        "ScoreTeam1,ScoreTeam2," + // Puntuación
                        "DistBallToMyGoal," +
                        "DistClosestAlly," +
                        "DistClosestEnemy," +
                        // LABELS (Acciones)
                        "InputX,InputZ," +
                        "ActionShoot,ActionPass"; // Por si añades botones después

        recordedLines.Add(header);
    }

    private void Update()
    {
        if (!recordMode) return;

        totalTime += Time.deltaTime;
        timeElapsed += Time.deltaTime;

        if (timeElapsed >= snapshotTime)
        {
            timeElapsed -= snapshotTime;
            RecordSnapshot();
        }
    }

    private void RecordSnapshot()
    {
        // 1. Posición del jugador
        Vector3 myPos = transform.position;

        // 2. Posición de la pelota
        Vector3 ballPos = Bola.instance != null ? Bola.instance.transform.position : Vector3.zero;

        // 3. Distancia a la pelota
        float distToBall = Vector3.Distance(myPos, ballPos);

        // 4. ¿Quién tiene la pelota? (Aproximación por ahora)
        int hasBallTeam = 0; // 0 = Nadie
        if (Bola.instance != null && Bola.instance.EnPosesion)
        {
            if (Bola.instance.Owner != null)
            {
               hasBallTeam = Bola.instance.Owner.CompareTag("Team1") ? 1 : 2; // Ajustar según tus tags
            }
        }

        // 5. Distancia a portería rival (simplificado: asumiendo Z positiva o negativa según el equipo)
        // Puedes ajustar esto según la posición estática de las porterías en tu campo
        float distToRivalGoal = 0f; // TODO: Calculate distances to actual goals

        // 6. Puntuaciones
        int scoreT1 = 0; // TODO: Get from arbitro/manager
        int scoreT2 = 0;

        // 7. Distancia pelota a mi portero
        float distBallToMyGoal = 0f;

        // 8 y 9. Distancia aliados y enemigos
        float distClosestAlly = 999f;
        float distClosestEnemy = 999f;

        PlayerID[] allPlayers = FindObjectsByType<PlayerID>(FindObjectsSortMode.None);
        foreach(PlayerID p in allPlayers)
        {
            if (p == myPlayer) continue;

            float d = Vector3.Distance(myPos, p.transform.position);
            
            // Asumiendo que usas Tags para diferenciar equipos
            if (p.CompareTag(myPlayer.tag))
            {
                if (d < distClosestAlly) distClosestAlly = d;
            }
            else
            {
                if (d < distClosestEnemy) distClosestEnemy = d;
            }
        }

        // --- LABELS (Acciones tomadas por el humano en CharacterGV) ---
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");

        // Faltaría mapear botones de tiro o pase si los usas
        int actionShoot = Input.GetKey(KeyCode.Space) ? 1 : 0; // Ejemplo
        int actionPass = Input.GetKey(KeyCode.LeftControl) ? 1 : 0; // Ejemplo


        // Construir la fila CSV
        string row = $"{totalTime:F2}," +
                     $"{myPos.x:F2},{myPos.z:F2}," +
                     $"{ballPos.x:F2},{ballPos.z:F2}," +
                     $"{distToBall:F2}," +
                     $"{hasBallTeam}," +
                     $"{distToRivalGoal:F2}," +
                     $"{scoreT1},{scoreT2}," +
                     $"{distBallToMyGoal:F2}," +
                     $"{distClosestAlly:F2}," +
                     $"{distClosestEnemy:F2}," +
                     $"{inputX:F2},{inputZ:F2}," +
                     $"{actionShoot},{actionPass}";

        recordedLines.Add(row);
    }

    // Método para guardar al terminar el nivel/partido
    public void SaveToFile()
    {
        if (!recordMode || recordedLines.Count <= 1) return;

        string date = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
        string finalPath = Path.Combine(Application.dataPath, $"{csvOutputName}_{date}.csv");

        File.WriteAllLines(finalPath, recordedLines);
        Debug.Log($"[Recorder] Dataset guardado con éxito en: {finalPath}");
    }

    private void OnApplicationQuit()
    {
        // Guardado automático al cerrar el juego como seguridad
        SaveToFile();
    }
}
