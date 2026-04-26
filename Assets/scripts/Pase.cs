using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public class Pase : MonoBehaviour
{

    private GameObject[] p;
    public static CharacterManager characterManager;
    public static CharacterGV characterGV;
    public static event Action <PlayerID> OnCharacterGVSelected;
    public static event Action<PlayerID> OnCharacterGVPass;
    public static Pase instance;
    private string tagPlayer;
    public List<PlayerID> jugadores = new List<PlayerID>();
    public List<PlayerID> jugadoresOrdenadosPorPorteria = new List<PlayerID>();

    private void Awake()
    {
        instance = this;
       
    }

        void Start()
    {
        p = GetAllFieldPlayers();
        characterManager = CharacterManager.instance;
    }

    
    void Update()
    {

        if (Input.GetKeyDown(KeyCode.P))
        {
            Vector3 pos = new Vector3(0f, 0f, 0f);
            string tagPlayer = "character";
            this.tagPlayer = tagPlayer;
            int team = 0;
            searchPlayersToPass(tagPlayer, pos, team);
        }

    }

     public void searchPlayersToPass(string tagPlayer, Vector3 pos, int team)
    {
      //Busco al jugador que la va a pasar
        int id = -1, i;

        this.tagPlayer = tagPlayer;

        if (tagPlayer == "portero")
        {
            GameObject[] porteros = GameObject.FindGameObjectsWithTag("Portero");

            for (i = 0; i < porteros.Length; i++)
            {
                id = p[i].GetComponent<PlayerID>().id;
                if (team % 2 == id % 2)
                {
                    break;
                }

            }

        } 
        else if (tagPlayer == "character")
        {
            for (i = 0; i < p.Length; i++)
            {
                id = p[i].GetComponent<PlayerID>().id;
                if (id == characterManager.index)
                {
                    break;
                }
            }
        } 
        else
        {
            for (i = 0; i < p.Length; i++)
            {
                id = p[i].GetComponent<PlayerID>().id;
                if (id == team) //El team es el id (lo he llamado así)
                {
                    break;
                }
            }
        }

        //if (calculateDistance(p[i].transform.position) < 25)
        //{
           
        //}

        passBall(id);
    }


    void passBall(int index)
    {
        //Index es el que la pasa
        // ID al que se la paso 
        GameObject player = p[0]; // Cojo un jugador (el primero de la lista)
        float minDistance = Mathf.Infinity;
        float currentDistance;

        int id = p[0].GetComponent<PlayerID>().id; //Cojo su id
        int j = 0; // Es la posición de la lista (no el id)
        int team = index % 2;
        string posicionPlayer = " ";
        
            for (int i = 0; i < p.Length; i++)
            {
                id = p[i].GetComponent<PlayerID>().id; //Veo si hay alguna con distancia menor 

            if (id != index && team == id % 2)
            {


                if (this.tagPlayer != "Defensa" && this.tagPlayer != "CentroCampista" && this.tagPlayer != "Delantero")
                {

                    currentDistance = calculateDistance(p[i].transform.position);
                    if (currentDistance < minDistance)
                    {
                        minDistance = currentDistance;
                        player = p[i];
                        j = i;
                    }

                }
                else
                {
                    posicionPlayer = p[i].GetComponent<PlayerID>().posicion;
                    if (this.tagPlayer == "Defensa")
                    {

                        if (id != index && team == id % 2 && posicionPlayer == this.tagPlayer)
                        {
                            currentDistance = calculateDistance(p[i].transform.position);
                            if (currentDistance < minDistance)
                            {
                                minDistance = currentDistance;
                                player = p[i];
                                j = i;
                            }
                        }
                    }
                    else if (this.tagPlayer == "CentroCampista")
                    {
                        if (id != index && team == id % 2 && posicionPlayer == this.tagPlayer)
                        {
                            currentDistance = calculateDistance(p[i].transform.position);
                            if (currentDistance < minDistance)
                            {
                                minDistance = currentDistance;
                                player = p[i];
                                j = i;
                            }
                        }
                    }
                    else if (this.tagPlayer == "Delantero")
                    {
                        if (id != index && team == id % 2 && posicionPlayer == this.tagPlayer)
                        {
                            currentDistance = calculateDistance(p[i].transform.position);
                            if (currentDistance < minDistance)
                            {
                                minDistance = currentDistance;
                                player = p[i];
                                j = i;
                            }
                        }
                    }
                }
                }
            }

            PlayerID playerID = player.GetComponent<PlayerID>();
        if (playerID.id != characterManager.index)
        {
            OnCharacterGVSelected?.Invoke(playerID);
        }

        Bola.instance.Soltar();


        Rigidbody rb = Bola.instance.GetComponent<Rigidbody>();
        rb.isKinematic = false;

        Vector3 direction = (player.transform.position - Bola.instance.transform.position).normalized;

         float passSpeed = 300f;
         rb.linearVelocity = direction * passSpeed;
         rb.angularVelocity = Vector3.zero;

     
    }

    public void passBallOwnPlayer()
    {
        for (int i = 0; i < p.Length; i++)
        {
            int id = p[i].GetComponent<PlayerID>().id;
            if (id == characterManager.index)
            {
                PlayerID playerID = p[i].GetComponent<PlayerID>();
               // OnCharacterGVPass?.Invoke(playerID);
                Bola.instance.Soltar();
                Rigidbody rb = Bola.instance.GetComponent<Rigidbody>();
                rb.isKinematic = false;
                Vector3 direction = (p[i].transform.position - Bola.instance.transform.position).normalized;
                float passSpeed = 300f;
                rb.linearVelocity = direction * passSpeed;
                rb.angularVelocity = Vector3.zero;
                break;
            }
        }
    }

    public void PassBallNear(Vector3 positionPlayer, Vector3 porteriaTeam, int teamID, PlayerID playerID)
    {

        GameObject bestPlayer = null;
        Vector3 dir = new Vector3(0, 0);

        float currentDistToGoal = Vector3.Distance(positionPlayer, porteriaTeam);
        float bestScore = Mathf.Infinity;

        for (int j = 0; j < p.Length; j++)
        {
            GameObject candidate = p[j];

            if (candidate == null) continue;

            PlayerID candidatePlayerID = candidate.GetComponent<PlayerID>();
            if (candidatePlayerID == null) continue;

           // if (candidatePlayerID.id == characterManager.index) continue;
            if (candidatePlayerID.id % 2 != teamID) continue;

            float candidateDistToGoal = Vector3.Distance(candidate.transform.position, porteriaTeam);
            float candidateDistToCurrent = Vector3.Distance(candidate.transform.position, positionPlayer);

            if (candidateDistToGoal >= currentDistToGoal) continue;
            float score = candidateDistToGoal + candidateDistToCurrent;

            if (score < bestScore)
            {
                bestScore = score;
                bestPlayer = candidate;
            }
          
        }

        Rigidbody rbPlayer = playerID.GetComponent<Rigidbody>();

        if (bestPlayer == null)
        {
            if (characterManager.index == playerID.id) return;
            playerID.tiroPorteria();
            return;
        }

        PlayerID bestPlayerID = bestPlayer.GetComponent<PlayerID>();

        OnCharacterGVSelected?.Invoke(bestPlayerID);

        Bola.instance.Soltar();

        Rigidbody rbBall = Bola.instance.GetComponent<Rigidbody>();

        Vector3 direction = (bestPlayer.transform.position - Bola.instance.transform.position);
        direction.y = 0f;

        if (direction != Vector3.zero)
        {
            playerID.transform.rotation = Quaternion.LookRotation(direction);
        }

        direction = direction.normalized;

        float passSpeed = 300f;
        rbBall.linearVelocity = direction * passSpeed;
        rbBall.angularVelocity = Vector3.zero;
        rbBall.WakeUp();
    }



    void OrdenarJugadoresPorDistancia()
    {
        jugadoresOrdenadosPorPorteria.Clear();
        jugadoresOrdenadosPorPorteria.AddRange(jugadores);

        jugadoresOrdenadosPorPorteria.Sort((a, b) =>
        {
            float da = (a.transform.position - transform.position).sqrMagnitude;
            float db = (b.transform.position - transform.position).sqrMagnitude;
            return da.CompareTo(db);
        });
    }

    public float calculateDistance(Vector3 myPos)
    {
        Vector3 ballPosition = Bola.instance.transform.position;
        myPos.y = 0;
        ballPosition.y = 0;
        return Vector3.Distance(myPos, ballPosition); 
    }

    private GameObject[] GetAllFieldPlayers()
    {
        List<GameObject> allPlayers = new List<GameObject>();

        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("Defensa"));
        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("CentroCampista"));
        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("Delantero"));

        return allPlayers.ToArray();
    }
}
