using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Graba automáticamente las posiciones absolutas de la IA (y la pelota)
/// durante el partido. Sirve para generar mapas de calor comparativos
/// con el dataset de entrenamiento humano.
///
/// Uso: adjunta este componente al mismo GameObject que el AIController
///      (Legacy, FSM o multi-modelo). Se activa solo al iniciar la escena
///      y guarda el CSV al salir.
/// </summary>
public class AIRecorder : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Intervalo entre snapshots en segundos (0.1 = 10 fps de datos)")]
    public float snapshotInterval = 0.1f;

    [Tooltip("Nombre base del fichero de salida")]
    public string csvOutputName = "AIData";

    [Tooltip("Si es false, no graba nada (útil para deshabilitar sin eliminar el componente)")]
    public bool recordingEnabled = true;

    [Header("References (auto-detectadas si se dejan vacías)")]
    public PlayerID myPlayer;

    // Internals
    private float _timer       = 0f;
    private int   _totalFrames = 0;
    private List<string> _lines = new List<string>();

    private static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    // ── Ciclo de vida ────────────────────────────────────────────────────────
    private void Start()
    {
        if (!recordingEnabled) return;

        if (myPlayer == null)
            myPlayer = GetComponent<PlayerID>();

        // Cabecera CSV — mismas columnas de posición absoluta que Recorder.cs
        _lines.Add("AbsMyPosX,AbsMyPosZ,AbsBallPosX,AbsBallPosZ," +
                   "TienePelota,InputX,InputZ,Disparo,Pase");

        Debug.Log($"[AIRecorder] Iniciado en {gameObject.name}");
    }

    private void Update()
    {
        if (!recordingEnabled) return;

        _timer += Time.deltaTime;
        if (_timer < snapshotInterval) return;
        _timer -= snapshotInterval;

        RecordSnapshot();
        _totalFrames++;
    }

    private void RecordSnapshot()
    {
        Vector3 myPos   = transform.position;
        Vector3 ballPos = Bola.instance != null
            ? Bola.instance.transform.position
            : Vector3.zero;

        // ¿Tiene la pelota?
        int hasBall = 0;
        if (Bola.instance != null && Bola.instance.EnPosesion &&
            Bola.instance.Owner != null)
        {
            PlayerID owner = Bola.instance.Owner.GetComponent<PlayerID>();
            if (owner != null && owner == myPlayer)
                hasBall = 1;
        }

        // Movimiento actual (velocity del Rigidbody normalizada a {-1,0,1})
        Rigidbody rb = GetComponent<Rigidbody>();
        float vx = 0f, vz = 0f;
        if (rb != null)
        {
            vx = rb.linearVelocity.x;
            vz = rb.linearVelocity.z;
            // Discretizar igual que el modelo: signo si supera umbral
            float threshold = 0.3f;
            vx = vx >  threshold ?  1f : vx < -threshold ? -1f : 0f;
            vz = vz >  threshold ?  1f : vz < -threshold ? -1f : 0f;
        }

        string F(float v) => v.ToString("F3", Inv);

        _lines.Add($"{F(myPos.x)},{F(myPos.z)}," +
                   $"{F(ballPos.x)},{F(ballPos.z)}," +
                   $"{hasBall}," +
                   $"{F(vx)},{F(vz)}," +
                   $"0,0");  // Disparo y Pase los detecta el propio AIController
    }

    // ── Guardado ─────────────────────────────────────────────────────────────
    public void SaveToFile()
    {
        if (!recordingEnabled || _lines.Count <= 1) return;

        string date  = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
        string path  = Path.Combine(Application.dataPath,
                                    $"{csvOutputName}_{date}.csv");
        File.WriteAllLines(path, _lines);
        Debug.Log($"[AIRecorder] {_totalFrames} snapshots → {path}");
    }

    private void OnApplicationQuit() => SaveToFile();
}
