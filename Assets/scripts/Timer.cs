using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Timer : MonoBehaviour
{

    private float timer = 0f;
    private bool matchEnded = false;
    private int matchSeconds;

    void Start()
    {
        matchEnded = false;
        matchSeconds = Data.instance.matchSeconds;
        Debug.Log(matchSeconds);
        ShowTime.instance.SetTime(Data.instance.matchSeconds);
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= 1f)
        {
            timer -= 1f;
            matchSeconds--;

            if (ShowTime.instance != null)
                ShowTime.instance.SetTime(matchSeconds);

            if (matchSeconds <= 0)
            {
                EndMatch();
            }
        }
    }

    void EndMatch()
    {
        Debug.Log("Fin del partido");
        Time.timeScale = 0f;
        matchEnded = true;

        if (Data.instance.esJuego)
        {
            SceneManager.LoadScene(6);
        }
        else
        {
            // El Recorder acumula las filas en memoria y vive en la escena de
            // jugadores, que se descarga con el LoadScene. Hay que rescatar el
            // CSV AQUI o se pierde entero.
            CapturarDatosDeEntrenamiento();
            SceneManager.LoadScene(8);
        }

    }

    /// <summary>
    /// Copia el dataset del Recorder activo a Data (que sobrevive al cambio de
    /// escena) y deja una copia de seguridad en disco.
    /// </summary>
    private void CapturarDatosDeEntrenamiento()
    {
        if (Data.instance == null)
        {
            Debug.LogWarning("[Timer] No hay Data.instance: no se puede pasar el dataset.");
            return;
        }

        // Hay un Recorder por jugador, pero solo uno ha grabado de verdad.
        // Nos quedamos con el que mas filas tenga.
        Recorder[] recorders = FindObjectsByType<Recorder>(FindObjectsSortMode.None);
        Recorder mejor = null;
        int maxLineas = 0;

        foreach (Recorder r in recorders)
        {
            if (r == null) continue;
            int n = r.GetRecordedLineCount();
            if (n > maxLineas)
            {
                maxLineas = n;
                mejor = r;
            }
        }

        if (mejor == null || maxLineas <= 0)
        {
            Data.instance.csvEntrenamiento = null;
            Data.instance.framesEntrenamiento = 0;
            Data.instance.rutaCsvBackup = null;
            Debug.LogWarning("[Timer] No hay datos grabados (¿se activó la grabación con R?). " +
                             "La pantalla de nombre no tendra nada que enviar.");
            return;
        }

        Data.instance.csvEntrenamiento = mejor.GetRecordedCSV();
        Data.instance.framesEntrenamiento = maxLineas;
        Data.instance.rutaCsvBackup = GuardarCopiaDeSeguridad(Data.instance.csvEntrenamiento);

        Debug.Log($"[Timer] {maxLineas} frames rescatados del Recorder para la pantalla de entrenamiento.");
    }

    /// <summary>
    /// Copia en persistentDataPath por si el envio al servidor falla o el
    /// juego se cierra: el dataset no se pierde nunca.
    /// </summary>
    private string GuardarCopiaDeSeguridad(string csv)
    {
        try
        {
            string nombre = "SoccerData_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss") + ".csv";
            string ruta = Path.Combine(Application.persistentDataPath, nombre);
            File.WriteAllText(ruta, csv);
            Debug.Log("[Timer] Copia de seguridad del dataset en: " + ruta);
            return ruta;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Timer] No se pudo guardar la copia de seguridad: " + e.Message);
            return null;
        }
    }

    public bool IsMatchEnded()
    {
        return matchEnded;
    }
}
