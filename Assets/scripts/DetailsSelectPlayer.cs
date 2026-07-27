using System;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


public class DetailsSelectPlayer : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

  
    public DetailsSelectPlayer instance;

    [SerializeField] private TextMeshProUGUI nombre;
    [SerializeField] private Image imagen;
    [SerializeField] private TextMeshProUGUI task;
    [SerializeField] private TextMeshProUGUI feature;
    [SerializeField] private TextMeshProUGUI function;
    [SerializeField] private int id;
    private CharacterManager characterManager;
    private int index;

    public Button seleccionarJugador;
    public Button seleccionarEntrenamiento;
    public Button aplicarModelo;
    public GameObject panelEntrenamiento;



    public void Awake()
    {
        instance = this;
    }


    public void Start()
    {
        characterManager = CharacterManager.instance;
        index = characterManager.index;
        Apply();
        ActualizarInterfaz();

    }

    private void Apply()
    {
        //imagen.sprite = characterManager.characterList[index].imagen;
        //nombre.text = characterManager.characterList[index].nombre;
        //task.text = characterManager.characterList[index].task;
        //feature.text = characterManager.characterList[index].feature;
        //function.text = characterManager.characterList[index].function;
    }


    // Update is called once per frame
    void Update()
    {
        
    }

    public void ActualizarInterfaz()
    {
        seleccionarJugador.gameObject.SetActive(false);
        seleccionarEntrenamiento.gameObject.SetActive(false);
        aplicarModelo.gameObject.SetActive(false);
        panelEntrenamiento.SetActive(false);

        switch (Data.instance.esJuego)
        {
            case true:
               
                seleccionarJugador.gameObject.SetActive(true);
                aplicarModelo.gameObject.SetActive(true);

                break;

            case false:
                seleccionarEntrenamiento.gameObject.SetActive(true);
                panelEntrenamiento.SetActive(true);
                break;
        }
    }

    public void SeleccionarJugador()
    {
        Data.instance.jugadorSeleccionado = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }

    public void SeleccionarModelo()
    {
       // Data.instance.jugadorSeleccionado = false;
    }

    public void SeleccionarEntrenamiento()
    {

    }

    public void Atras()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }
}
