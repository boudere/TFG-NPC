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

    private CharacterManager characterManager;
    public int index;
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
        ActualizarInterfaz();
        if (index > characterManager.characterList.Count - 1) { index = 0; }
    }


    public void ActualizarInterfaz()
    {
        imagenesEntrenamiento.enabled = false;
        imagenesJugador.enabled = false;
        imagenesModelo.enabled = false;

        switch (Data.instance.esJuego)
        {
            case true:
                imagenesJugador.enabled = true;
                imagenesModelo.enabled = true;

                break;

            case false:
                imagenesEntrenamiento.enabled = true;
                break;
        }
    }

    public void Atras()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }



    public void detailsPlayer(GameObject boton)
    {
      
            characterManager.index = boton.GetComponent<BotonPosicion>().id;
            Debug.Log(characterManager.index);
            index = boton.GetComponent<BotonPosicion>().id;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
        


    }


    public void PlayStart()
    {
        Debug.Log(Data.instance.jugadorSeleccionado);

        if (!Data.instance.jugadorSeleccionado)
            return;


        Debug.Log(Data.instance.jugadorSeleccionado);
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
        Destroy(InfoCharacter.instance.gameObject);


    }
}
