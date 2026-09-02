using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Diagnostics;
using System.IO;

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


        for (int i = 0; i < p.Length; i++)
        {
            Recorder recorder = p[i].GetComponent<Recorder>();
            CharacterGV characterGV = p[i].GetComponent<CharacterGV>();
            TrainingClient trainingClient = p[i].GetComponent<TrainingClient>();
            AIControllerFNNClasi aiControllerFNNClasi = p[i].GetComponent<AIControllerFNNClasi>();
            AIControllerGRU aiControllerGRU = p[i].GetComponent<AIControllerGRU>();
            AIControllerSlidingWindow aiControllerSliding = p[i].GetComponent<AIControllerSlidingWindow>();
            AIRecorder aiRecorder = p[i].GetComponent<AIRecorder>();

            if (recorder != null)
                recorder.enabled = false;

            if (characterGV != null)
                characterGV.enabled = false;

            if (trainingClient != null)
                trainingClient.enabled = false;

            if (aiControllerFNNClasi != null)
                aiControllerFNNClasi.enabled = false;

            // Las tres arquitecturas se apagan siempre: mas abajo solo se
            // enciende la elegida. Si quedaran dos activas se pelearian por el
            // mismo Rigidbody y el agente se moveria de forma erratica.
            if (aiControllerGRU != null)
                aiControllerGRU.enabled = false;

            if (aiControllerSliding != null)
                aiControllerSliding.enabled = false;

            if (aiRecorder != null) 
                aiRecorder.enabled = false;
        }

        if (Data.instance.jugadorSeleccionadoEntrenamiento)
        {
            for (int i = 0; i < p.Length; i++)
            {
                PlayerID playerID = p[i].GetComponent<PlayerID>();

                if (playerID != null && playerID.id == characterManager.index)
                {
                    Recorder recorder = p[i].GetComponent<Recorder>();
                    CharacterGV characterGV = p[i].GetComponent<CharacterGV>();
                    TrainingClient trainingClient = p[i].GetComponent<TrainingClient>();

                    if (recorder != null)
                        recorder.enabled = true;

                    if (characterGV != null)
                        characterGV.enabled = true;

                    if (trainingClient != null)
                        trainingClient.enabled = true;

                    break; 
                }
            }
        }

        if (Data.instance.jugadorSeleccionado)
        {
            for (int i = 0; i < p.Length; i++)
            {
                PlayerID playerID = p[i].GetComponent<PlayerID>();

                if (playerID != null && playerID.id == characterManager.index)
                {
                    CharacterGV characterGV = p[i].GetComponent<CharacterGV>();
                  
                    if (characterGV != null)
                        characterGV.enabled = true;

                  

                    break;
                }
            }
        }


        if (Data.instance.jugadorAplicarModelo)
        {
            for (int i = 0; i < p.Length; i++)
            {
                PlayerID playerID = p[i].GetComponent<PlayerID>();

                if (playerID != null && playerID.id == characterManager.indexModel)
                {
                    
                    AIControllerFNNClasi aiControllerFNNClasi = p[i].GetComponent<AIControllerFNNClasi>();
                    AIControllerGRU aiControllerGRU = p[i].GetComponent<AIControllerGRU>();
                    AIControllerSlidingWindow aiControllerSliding = p[i].GetComponent<AIControllerSlidingWindow>();
                    AIRecorder aiRecorder = p[i].GetComponent<AIRecorder>();

                    // Data.arquitecturaModelo decide cual de las tres juega.
                    // 0 es el FNN, que es el flujo normal del juego; 1 y 2 son
                    // la GRU y la ventana deslizante, que existen para la
                    // comparativa del TFG y cargan su modelo del inspector.
                    int arq = Data.instance.arquitecturaModelo;

                    if (arq == 1 && aiControllerGRU != null)
                    {
                        aiControllerGRU.enabled = true;
                    }
                    else if (arq == 2 && aiControllerSliding != null)
                    {
                        aiControllerSliding.enabled = true;
                    }
                    else if (aiControllerFNNClasi != null)
                    {
                        if (arq != 0)
                        {
                            Debug.LogWarning($"[CharacterManagerInField] arquitecturaModelo={arq} " +
                                             "pero ese controlador no esta en el jugador. Se usa el FNN.");
                        }

                        // Antes de habilitarlo, para que su Start ya encuentre
                        // la eleccion hecha y no cargue el modelo por defecto.
                        if (!string.IsNullOrEmpty(Data.instance.rutaModeloONNX))
                        {
                            aiControllerFNNClasi.AsignarModelo(
                                Data.instance.rutaModeloONNX,
                                Data.instance.rutaScalerModelo);
                        }

                        aiControllerFNNClasi.enabled = true;
                    }


                    if (aiRecorder != null)
                        aiRecorder.enabled = true;

                    break;
                }
            }
        }




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





    