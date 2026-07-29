using UnityEngine;

public class Data : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    public bool esJuego = false;
    public bool jugadorSeleccionado = false;
    public bool jugadorSeleccionadoEntrenamiento = false;
    public bool jugadorAplicarModelo = false;
    public string rutaModeloONNX;

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
