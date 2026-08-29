using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


public class DetailsSelectPlayer : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

  
   // public DetailsSelectPlayer instance;

   
    [SerializeField] private Image imagen;
    [SerializeField] private Image imagenCromo;
    [SerializeField] private Image imagenLabel;
    //[SerializeField] private TextMeshProUGUI nombre;
    [SerializeField] private TextMeshProUGUI task;
    [SerializeField] private TextMeshProUGUI feature;
    [SerializeField] private TextMeshProUGUI function;
    [SerializeField] private int id;
    [SerializeField] private GameObject infoPanel;
    [SerializeField] private TMP_InputField inputField;

    
    private CharacterManager characterManager;


    [SerializeField] private GameObject panelInfoPlayer;

    private Image fondo;
    //private Image fondoCromo;

    public Sprite fondoAzul;
    public Sprite fondoRojo;


    public Sprite fondoCromoAzul;
    public Sprite fondoCromoRojo;


    public Button seleccionarJugador;
    public Button seleccionarEntrenamiento;
    public Button aplicarModelo;

    public string rutaModeloONNX;

    private SelectorDeModelo selector;

    public void Awake()
    {
       // instance = this;
    }


    public void Start()
    {
        panelInfoPlayer.SetActive(false);
        fondo = GetComponent<Image>();
        characterManager = CharacterManager.instance;
        // index = characterManager.index;
        Apply(characterManager.select);

        if (characterManager.select % 2 == 0)
        {
            fondo.sprite = fondoAzul;
            imagenCromo.sprite = fondoCromoAzul;
        }
        else
        {
            fondo.sprite = fondoRojo;
            imagenCromo.sprite = fondoCromoRojo;
        }
        ActualizarInterfaz();

    }

    private void Apply(int index)
    {
        imagen.sprite = characterManager.characterList[index].imagen;
        imagenLabel.sprite = characterManager.characterList[index].label;
        task.text = characterManager.characterList[index].task;
        feature.text = characterManager.characterList[index].feature;
        function.text = characterManager.characterList[index].function;
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
     

        switch (Data.instance.esJuego)
        {
            case true:
               
                seleccionarJugador.gameObject.SetActive(true);
                aplicarModelo.gameObject.SetActive(true);

                break;

            case false:
                seleccionarEntrenamiento.gameObject.SetActive(true);
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
        if (characterManager.index == characterManager.select)
        {
            UnityEngine.Debug.Log("Jugador ya asociado");
            return;
        }

        // Antes esto abria el explorador de archivos de Windows. Ese dialogo
        // arrastraba el shell al proceso y con el un onnxruntime.dll
        // incompatible que cerraba el juego, ademas de dejar elegir modelos de
        // otra arquitectura o con el scaler descuadrado. Ahora se elige de una
        // lista con los modelos que el propio juego ha entrenado.
        if (selector == null) selector = gameObject.AddComponent<SelectorDeModelo>();

        selector.Abrir(AplicarModeloElegido, null);
    }

    private void AplicarModeloElegido(ModeloInfo modelo)
    {
        if (modelo == null) return;

        characterManager.indexModel = characterManager.select;

        Data.instance.rutaModeloONNX = modelo.rutaOnnx;
        Data.instance.rutaScalerModelo = modelo.rutaScaler;
        Data.instance.nombreModeloAplicado = modelo.nombre;

        UnityEngine.Debug.Log("Modelo elegido: " + modelo.nombre + " -> " + modelo.rutaOnnx);

        Data.instance.jugadorAplicarModelo = true;
        Data.instance.jugadorSeleccionadoEntrenamiento = false;

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }

    public void SeleccionarEntrenamiento()
    {
        characterManager.index = characterManager.select;
        //infoPanel.SetActive(true);

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
        Data.instance.jugadorSeleccionadoEntrenamiento = true;
        Data.instance.jugadorAplicarModelo = false;
        Data.instance.jugadorSeleccionado = false;
    }

    public void showInfo()
    {
        panelInfoPlayer.SetActive(true);
    }

    public void dismissInfo()
    {
        panelInfoPlayer.SetActive(false);
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
        Data.instance.jugadorSeleccionadoEntrenamiento = false;
        Data.instance.jugadorAplicarModelo = false;
        Data.instance.jugadorSeleccionado = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }
}
