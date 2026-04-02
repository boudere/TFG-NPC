using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CharacterManagerInField : MonoBehaviour
{
    public static CharacterManager characterManager;
   [SerializeField] Camera camY;
    private GameObject[] players;

    void Start()
    {
        
        characterManager = CharacterManager.instance;
        GameObject[] p = GetAllFieldPlayers();

        foreach (GameObject player in p) {
          PlayerID pl = player.GetComponent<PlayerID>();
        }

        players = new GameObject[p.Length];

        for (int i = 0; i < p.Length; i++)
        {
            foreach (var go in p)
            {
                int id = go.GetComponent<PlayerID>().id;
                if (id == characterManager.index)
                {
                    players[id] = go;
                    break;
                }
            }
        }
        cameraConfiguration(characterManager.index);
    }

    private void Update()
    {
        cameraConfiguration(characterManager.index);
    }

    private GameObject[] GetAllFieldPlayers()
    {
        List<GameObject> allPlayers = new List<GameObject>();

        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("Defensa"));
        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("CentroCampista"));
        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("Delantero"));

        return allPlayers.ToArray();
    }

    public void cameraConfiguration(int i)
    {
        //Cambiar a 300 
        camY.transform.position = new Vector3(players[i].transform.position.x, 600, players[i].transform.position.z);
    }
    



}





    