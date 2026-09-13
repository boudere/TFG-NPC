using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CharacterSelector : MonoBehaviour
{
    public static CharacterSelector instance;
    [SerializeField] private TextMeshProUGUI nombre;
    [SerializeField] private Image imagen;
    [SerializeField] private TextMeshProUGUI task;
    [SerializeField] private TextMeshProUGUI feature;
    [SerializeField] private TextMeshProUGUI function;
    [SerializeField] private int id;
    private CharacterManager characterManager;
    private CharacterManagerInField characterManagerInField;
    [SerializeField] private Material defaultMaterial;
    [SerializeField] private Material newMaterial;
    [SerializeField] private bool selected;
    [SerializeField] private GameObject selectedGameObject;
    private int index;
    private AsyncOperation _async;
    void Start()
    {
        characterManager = CharacterManager.instance;
        index = characterManager.index;
        Apply();
        resetMaterial();

        if (index > characterManager.characterList.Count - 1) { index = 0; }
    }

    void Update()
    {
        
    }

    private void resetMaterial()
    {
        for (int i = 0; i < characterManager.characterList.Count; i++) {
            putDefaultMaterial(i);
            characterManager.characterList[i].selected = false;
        }
    }

    private void Apply()
    {
        imagen.sprite = characterManager.characterList[index].imagen; 
        nombre.text = characterManager.characterList[index].nombre; 
        task.text = characterManager.characterList[index].task; 
        feature.text = characterManager.characterList[index].feature;
        function.text = characterManager.characterList[index].function;
        selectedGameObject = characterManager.characterList[index].personajeJugable;
    }

    private void CambiarPantalla() {
        characterManager.index = index;
        Apply();
    }

    public void SiguientePersonaje() {
        if (index == characterManager.characterList.Count - 1) { 
            index = 0;  
           
        }
        else {
         
            index += 1;
        }
        CambiarPantalla();
  
    }

    public void AnteriorPersonaje() {

        if (index == 0)
        {
            index = characterManager.characterList.Count - 1;
        }
        else
        {
            index -= 1;
        }
        CambiarPantalla();
    }

    public void BackInfo() {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 2);
    }

    public void Info() {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 2);
    }
  


    public void PlayStart()
    {
        resetMaterial();
        putNewMaterial(characterManager.index);
    

        characterManager.index = index;

        for (int i = 0; i < characterManager.characterList.Count; i++)
        {

        }

        for (int i = 0; i < characterManager.characterList.Count; i++)
        {
           
            if (characterManager.characterList[i].id == index)
            {
                characterManager.characterList[i].selected = true;
                characterManager.pl = selectedGameObject;
                break;
            }
        }

        
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
        SceneManager.LoadScene(
    SceneManager.GetActiveScene().buildIndex + 3,
    LoadSceneMode.Additive
);



   

    }

   

    public void PlayStart2()
    {
    //    resetMaterial();
    //    putNewMaterial(characterManager.index);


    //    characterManager.index = index;

    //    for (int i = 0; i < characterManager.characterList.Count; i++)
    //    {

    //        if (characterManager.characterList[i].id == index)
    //        {
    //            characterManager.characterList[i].selected = true;
    //            break;
    //        }
    //    }


    //    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 5);
    //    SceneManager.LoadScene(
    //SceneManager.GetActiveScene().buildIndex + 6,
    //LoadSceneMode.Additive
 //);



    }



    private void putNewMaterial(int index)
    {
        //GameObject go = characterManager.characterList[index].personajeJugable;
        //Renderer rend = go.GetComponent<Renderer>();
        //rend.material = newMaterial;
    }

    private void putDefaultMaterial(int index)
    {
        //GameObject go = characterManager.characterList[index].personajeJugable;
        //Renderer rend = go.GetComponent<Renderer>();
        //rend.material = defaultMaterial;
    }


}