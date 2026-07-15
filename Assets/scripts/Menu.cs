using UnityEngine;

public class Menu : MonoBehaviour
{
    public GameObject menuPanel;
    public GameObject menuExit;
    public GameObject menuClose;
    public GameObject modoJuego;
    public GameObject modoEntrenamiento;
    public GameObject salir;
    public static bool esModoJuego = true;

    public void MostrarMenu()
    {
        menuPanel.SetActive(true);
        menuExit.SetActive(true);
        menuClose.SetActive(true);
        modoJuego.SetActive(true);
        modoEntrenamiento.SetActive(true);
        salir.SetActive(true);
    }

    public void OcultarMenu()
    {
        menuPanel.SetActive(false);
        menuExit.SetActive(false);
        menuClose.SetActive(false);
        modoJuego.SetActive(false);
        modoEntrenamiento.SetActive(false);
        salir.SetActive(false);
    }

    public void SeleccionarModoJuego()
    {
        esModoJuego = true;
    }

    public void SeleccionarModoEntrenamiento()
    {
        esModoJuego = false;
    }

}
