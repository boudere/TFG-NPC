using UnityEngine;
using UnityEngine.SceneManagement;

public class ChooseOption : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    public bool juego = false;
    public static ChooseOption instance;

    private void Awake()
    {
     
    }

    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Atras()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }

    public void ModoJuego()
    {
        Debug.Log("Se ha pulsado ModoJuego");

        Data.instance.esJuego = true;

        Debug.Log("esJuego ahora vale: " + Data.instance.esJuego);

        SceneManager.LoadScene(
            SceneManager.GetActiveScene().buildIndex + 1
        );
    }

    public void ModoEntrenamiento()
    {
        Debug.Log("Se ha pulsado ModoEntrenamiento");

        Data.instance.esJuego = false;

        Debug.Log("esJuego ahora vale: " + Data.instance.esJuego);

        SceneManager.LoadScene(
            SceneManager.GetActiveScene().buildIndex + 1
        );
    }


}
