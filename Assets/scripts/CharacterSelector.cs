

using NUnit.Framework.Internal.Commands;
using System;
using System.Collections;
using TMPro;
using Unity.VisualScripting;
using UnityEditor.Build.Content;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
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
    private int index;
    private bool characterLoaded = false;
    private bool fieldLoaded = false;
    private AsyncOperation _async;
    void Start()
    {
        characterManager = CharacterManager.instance;
        index = characterManager.index;
        Apply();
        resetMaterial();

        if (index > characterManager.characterList.Count - 1) { index = 0; }
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
        characterManager.characterList[index].selected = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
        SceneManager.LoadScene(
    SceneManager.GetActiveScene().buildIndex + 3,
    LoadSceneMode.Additive
);

   

    }



    private void putNewMaterial(int index)
    {
        GameObject go = characterManager.characterList[index].personajeJugable;
        Renderer rend = go.GetComponent<Renderer>();
        rend.material = newMaterial;
    }

    private void putDefaultMaterial(int index)
    {
        GameObject go = characterManager.characterList[index].personajeJugable;
        Renderer rend = go.GetComponent<Renderer>();
        rend.material = defaultMaterial;
    }
}