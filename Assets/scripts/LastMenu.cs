using UnityEngine;
using UnityEngine.SceneManagement;

public class LastMenu : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void volverJugar()
    {
        CharacterManager.instance.index = -1;
        CharacterManager.instance.indexModel = -1;
        Data.instance.jugadorSeleccionado = false;
        Data.instance.jugadorAplicarModelo = false;
        Data.instance.jugadorSeleccionadoEntrenamiento = false;
        SceneManager.LoadScene(1);
    }

    public void salir()
    {
        Application.Quit();
    }
}
