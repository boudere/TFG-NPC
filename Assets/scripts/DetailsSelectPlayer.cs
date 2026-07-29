
using System.Diagnostics;
using System.IO;
using System;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SFB;





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
    [SerializeField] private GameObject infoPanel;
    [SerializeField] private TMP_InputField inputField;
    private CharacterManager characterManager;
    private int index;

    public Button seleccionarJugador;
    public Button seleccionarEntrenamiento;
    public Button aplicarModelo;
    public GameObject panelEntrenamiento;

    public string rutaModeloONNX;

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
        infoPanel.SetActive(false);
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
        if (characterManager.indexModel != characterManager.select)
        {
            characterManager.index = characterManager.select;
        } else
        {
            UnityEngine.Debug.Log("Jugador ya asociado");
        }
            Data.instance.jugadorSeleccionado = true;
        Data.instance.jugadorSeleccionadoEntrenamiento = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }

    public void SeleccionarModelo()
    {
        if (characterManager.index != characterManager.select)
        {
            characterManager.indexModel = characterManager.select;
        }
        else
        {
            UnityEngine.Debug.Log("Jugador ya asociado");
            return;
        }

        var paths = StandaloneFileBrowser.OpenFilePanel(
            "Selecciona un modelo ONNX",
            "",
            new[] { new ExtensionFilter("Modelo ONNX", "onnx") },
            false);

        if (paths.Length == 0)
        {
            UnityEngine.Debug.Log("No se seleccionó ningún modelo.");
            return;
        }

        rutaModeloONNX = paths[0];

        UnityEngine.Debug.Log("Modelo seleccionado: " + rutaModeloONNX);

        Data.instance.rutaModeloONNX = rutaModeloONNX; // Si quieres usarla en otra escena

        Data.instance.jugadorAplicarModelo = true;
        Data.instance.jugadorSeleccionadoEntrenamiento = false;

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }

    public void SeleccionarEntrenamiento()
    {
        infoPanel.SetActive(true);
       
        //SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }

    public void aceptar()
    {
        Data.instance.textoPanel = inputField.text;
        Data.instance.jugadorSeleccionadoEntrenamiento = true;
        Data.instance.jugadorAplicarModelo = false;
        Data.instance.jugadorSeleccionado = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }

    public void cancelar()
    {
        infoPanel.SetActive(false);
    }

    public void Atras()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }
}
