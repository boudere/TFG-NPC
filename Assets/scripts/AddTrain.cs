using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class AddTrain : MonoBehaviour
{


    [SerializeField] private GameObject infoPanel;
    [SerializeField] private TMP_InputField inputField;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    public void aceptar()
    {
       
        Data.instance.textoPanel = inputField.text;
        SceneManager.LoadScene(1);
       
    }

   

}
