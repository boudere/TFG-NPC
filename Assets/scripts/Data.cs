using UnityEngine;

public class Data : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    public bool esJuego = false;
    public bool jugadorSeleccionado = false;
    public bool jugadorSeleccionadoEntrenamiento = false;
    public bool jugadorAplicarModelo = false;
    public string rutaModeloONNX;
    public string textoPanel;
    public int blueTeam = 0;
    public int matchSeconds = 300;
public int redTeam = 0;

    // ------------------------------------------------------------------
    // Dataset rescatado al terminar un entrenamiento.
    // Lo rellena Timer.EndMatch() ANTES de cambiar de escena, porque el
    // Recorder guarda las filas en memoria y muere al descargarse la escena
    // de jugadores. Lo consume AddTrain en la pantalla del nombre.
    //
    // NonSerialized a proposito: el CSV puede pesar megas y no queremos que
    // Unity intente serializarlo en la escena ni pintarlo en el inspector.
    // ------------------------------------------------------------------
    [System.NonSerialized] public string csvEntrenamiento;
    [System.NonSerialized] public int framesEntrenamiento;
    [System.NonSerialized] public string rutaCsvBackup;

    public static Data instance;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
