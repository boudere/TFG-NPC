using System;
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
