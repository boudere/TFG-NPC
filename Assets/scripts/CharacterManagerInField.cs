using System;
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
        GameObject[] p = GameObject.FindGameObjectsWithTag("Player");
        players = new GameObject[p.Length];

        for (int i = 0; i < p.Length; i++)
        {
            foreach (var go in p)
            {
                int id = go.GetComponent<PlayerID>().id;
                Debug.Log(id);
                Debug.Log(characterManager.index);

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

    public void cameraConfiguration(int i)
    {
        camY.transform.position = new Vector3(players[i].transform.position.x, 450, players[i].transform.position.z);
    }
    



}





    