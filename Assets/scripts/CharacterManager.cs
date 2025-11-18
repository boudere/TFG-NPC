using System;
using System.Collections.Generic;
using UnityEngine;

public class CharacterManager : MonoBehaviour
{
    public static string LastSelectedTag { get; private set; }
    public static int LastSelectedCount { get; private set; }

    public static CharacterManager instance;
    public bool selected;
    public List<Character> characterList;
    public int index = 0;   // índice del personaje actualmente seleccionado

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);   // se mantiene al cambiar de escena
        }
        else
        {
            Destroy(gameObject);
        }
    }

    
  
   

    // Esto es algo que ya tenías, lo dejo porque quizá lo uses para otra cosa
    public static void RegisterSelection(string tag, int count)
    {
        LastSelectedTag = tag;
        LastSelectedCount = count;
    }
}