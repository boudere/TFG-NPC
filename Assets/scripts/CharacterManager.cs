using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CharacterManager : MonoBehaviour
{
    public static string LastSelectedTag { get; private set; }
    public static int LastSelectedCount { get; private set; }

    public static CharacterManager instance;
    public bool selected;
    public List<Character> characterList;
    public int index;
    public int indexModel;
    public int select;
   
    public CharacterGV[] characters;
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);   
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        index = -1;
        indexModel = -1;
        select = -1;
    }
}