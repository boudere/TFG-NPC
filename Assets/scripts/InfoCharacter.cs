using System;
using TMPro;
using UnityEngine;
using UnityEngine.ProBuilder.MeshOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class InfoCharacter : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    public bool jugadorSeleccionado = false;
    public bool jugadorModeloAplicado = false;


    public Image imagenesEntrenamiento;
    public Image imagenesJugador;
    public Image imagenesModelo;

    public Image ImageSeleccionadoJuego;
    public Image ImageSeleccionadoEntreno;
    public Image ImageModeloAplicado;

    [SerializeField] private Sprite fichaNoSeleccionado;

    [SerializeField] private GameObject ajustes;
    [SerializeField] private GameObject ajustesTiempo;


    public TMP_InputField inputTime;

    private int timeValue;



    private CharacterManager characterManager;
    public int index;
    public int indexModel;
    public static InfoCharacter instance;


    private void Awake()
    {
        //if (instance == null)
        //{
        //    instance = this;
        //    DontDestroyOnLoad(gameObject);
        //}
        //else
        //{
        //    Destroy(gameObject);
        //}
    }

    void Start()
    {
        Data.instance.matchSeconds = 300;
      characterManager = CharacterManager.instance;
        index = characterManager.index;
        indexModel = characterManager.indexModel;
   
        if (index > characterManager.characterList.Count - 1) { index = -1; }

        ValidarIndices();
        ActualizarInterfaz();
        ActualizarFichas();
    }


    public void ActualizarInterfaz()
    {
        imagenesEntrenamiento.enabled = false;
        imagenesJugador.enabled = false;
        imagenesModelo.enabled = false;
        ImageSeleccionadoJuego.enabled = false;
        ImageSeleccionadoEntreno.enabled = false;
        ImageModeloAplicado.enabled = false;

        switch (Data.instance.esJuego)
        {
            case true:
                imagenesJugador.enabled = true;
                imagenesModelo.enabled = true;
                ImageSeleccionadoJuego.enabled = true;
                ImageModeloAplicado.enabled = true;

                break;

            case false:
                imagenesEntrenamiento.enabled = true;
                ImageSeleccionadoEntreno.enabled = true;
                break;
        }
    }

    private void ActualizarFichas()
    {
      
        ImageSeleccionadoJuego.sprite = fichaNoSeleccionado;
        ImageSeleccionadoEntreno.sprite = fichaNoSeleccionado;
        ImageModeloAplicado.sprite = fichaNoSeleccionado;

       
        if (index >= 0 && index < characterManager.characterList.Count)
        {
            Sprite fichaJugador =
                characterManager.characterList[index].selectImagen;

            if (Data.instance.esJuego)
            {
                ImageSeleccionadoJuego.sprite = fichaJugador;
            }
            else
            {
                ImageSeleccionadoEntreno.sprite = fichaJugador;
            }
        }

    
        if (indexModel >= 0 &&
            indexModel < characterManager.characterList.Count)
        {
            ImageModeloAplicado.sprite =
                characterManager.characterList[indexModel].selectImagen;
        }
    }


    private void ValidarIndices()
    {
        // Índice del jugador
        if (index < 0 || index >= characterManager.characterList.Count)
        {
            index = -1;
            characterManager.index = -1;
        }

        // Índice del modelo
        if (indexModel < 0 ||
            indexModel >= characterManager.characterList.Count)
        {
            indexModel = -1;
            characterManager.indexModel = -1;
        }
    }


    public void Atras()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
        characterManager.indexModel = -1;
        characterManager.index = -1;
        
    }



    public void detailsPlayer(GameObject boton)
    {
      
            characterManager.select = boton.GetComponent<BotonPosicion>().id;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);

        


    }

    public void openMenu()
    {
        ajustes.SetActive(true);
    }


    public void cerrarMenu()
    {
        ajustes.SetActive(false);
    }

    public void openSetTime()
    {
        ajustesTiempo.SetActive(true);
    }


    public void cerrarSetTime()
    {
        ajustesTiempo.SetActive(false);
    }

    public void AceptarSetTime()
    {
        if (string.IsNullOrWhiteSpace(inputTime.text))
        {
            timeValue = 300;
            Debug.Log("Sin valor. Se asigna el tiempo por defecto: 300");
        }
        else if (int.TryParse(inputTime.text, out int valor))
        {
            timeValue = valor;
            Debug.Log(timeValue);
        }
        else
        {
            Debug.LogWarning("Introduce un valor numérico válido.");
            return;
        }

        Data.instance.matchSeconds = timeValue;
        ajustesTiempo.SetActive(false);
    }

    public void PlayStart()
    {

        if (Data.instance.esJuego && !Data.instance.jugadorSeleccionado )
            return;

        if (!Data.instance.esJuego && !Data.instance.jugadorSeleccionadoEntrenamiento)
            return;

        for (int i = 0; i < characterManager.characterList.Count; i++)
        {

            if (characterManager.characterList[i].id == index)
            {
                characterManager.characterList[i].selected = true;
                break;
            }
        }

        
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 2);
        SceneManager.LoadScene(
    SceneManager.GetActiveScene().buildIndex + 3,
    LoadSceneMode.Additive
);
  
    }
}
