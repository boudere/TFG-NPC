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
        Data.instance.matchSeconds = 600;   // 10 min, en entrenamiento y en juego
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
            timeValue = 600;
            Debug.Log("Sin valor. Se asigna el tiempo por defecto: 600 (10 min)");
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
        // Antes esto miraba los flags de Data (jugadorSeleccionado /
        // jugadorSeleccionadoEntrenamiento) y hacia un return mudo si estaban a
        // false. El problema: esos flags y los indices que pintan las fichas en
        // pantalla eran dos memorias distintas que nadie mantenia
        // sincronizadas, asi que veias los jugadores puestos y el boton no
        // hacia nada, sin decir por que.
        //
        // Ahora se lee EL MISMO dato que dibuja la interfaz, y los flags se
        // derivan aqui, en el unico momento en que importan. No pueden
        // discrepar porque ya no son una fuente de verdad aparte.
        if (characterManager == null) characterManager = CharacterManager.instance;

        if (characterManager == null)
        {
            MostrarAviso("No se puede iniciar la partida: falta el gestor de personajes.");
            return;
        }

        if (characterManager.index < 0 ||
            characterManager.index >= characterManager.characterList.Count)
        {
            MostrarAviso(Data.instance.esJuego
                ? "Elige primero el jugador que vas a controlar."
                : "Elige primero el jugador con el que vas a entrenar.");
            return;
        }

        Data.instance.jugadorSeleccionado = Data.instance.esJuego;
        Data.instance.jugadorSeleccionadoEntrenamiento = !Data.instance.esJuego;
        Data.instance.jugadorAplicarModelo =
            Data.instance.esJuego && characterManager.indexModel >= 0;

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

    // ======================================================================
    // Aviso en pantalla. Un boton que no responde y no explica nada es el
    // peor error posible en una demostracion: parece que el juego esta roto.
    // IMGUI para no depender de montar nada en la escena.
    // ======================================================================
    private string _aviso = "";
    private float _avisoTimer;
    private GUIStyle _avisoCaja, _avisoTexto;
    private Texture2D _avisoBg;
    private bool _avisoEstilos;

    private const float AVISO_DURACION = 4f;

    private void MostrarAviso(string texto)
    {
        _aviso = texto;
        _avisoTimer = AVISO_DURACION;
        UnityEngine.Debug.LogWarning("[InfoCharacter] " + texto);
    }

    private void Update()
    {
        if (_avisoTimer > 0f)
        {
            _avisoTimer -= Time.unscaledDeltaTime;
            if (_avisoTimer <= 0f) _aviso = "";
        }
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(_aviso)) return;

        if (!_avisoEstilos)
        {
            _avisoBg = new Texture2D(1, 1);
            _avisoBg.SetPixel(0, 0, new Color(0.28f, 0.07f, 0.07f, 0.95f));
            _avisoBg.Apply();

            _avisoCaja = new GUIStyle(GUI.skin.box);
            _avisoCaja.normal.background = _avisoBg;

            _avisoTexto = new GUIStyle(GUI.skin.label);
            _avisoTexto.fontSize = 15;
            _avisoTexto.wordWrap = true;
            _avisoTexto.alignment = TextAnchor.MiddleCenter;
            _avisoTexto.normal.textColor = Color.white;

            _avisoEstilos = true;
        }

        int w = 560;
        int h = 70;
        int x = (Screen.width - w) / 2;
        int y = Screen.height - h - 40;

        GUI.Box(new Rect(x, y, w, h), "", _avisoCaja);
        GUI.Label(new Rect(x + 16, y + 10, w - 32, h - 20), _aviso, _avisoTexto);
    }
}
