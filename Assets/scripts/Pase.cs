using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public class Pase : MonoBehaviour
{

    private GameObject[] players;
    public static CharacterManager characterManager;
    public static CharacterGV characterGV;
    public static event Action <GameObject> OnCharacterGVSelected;
    public static Pase instance;

    private void Awake()
    {
        instance = this;
    }

        void Start()
    {
        characterManager = CharacterManager.instance;
    }

    
    void Update()
    {

        if (Input.GetKeyDown(KeyCode.P))
        {


            Vector3 pos = new Vector3(0f, 0f, 0f);
            string tagPlayer = "npc";
            int team = 0;
            searchPlayersToPass(tagPlayer, pos, team);
        }

    }

     public void searchPlayersToPass(string tagPlayer, Vector3 pos, int team)
    {
        GameObject[] p;
        int id = -1, i;

        if (tagPlayer == "portero" )
        {
            p = GameObject.FindGameObjectsWithTag("Portero");

            for (i = 0; i < p.Length; i++)
            {
                id = p[i].GetComponent<PlayerID>().id;
                if (team % 2 == id % 2)
                {
                    break;
                }

            }

        } else
        {
             p = GameObject.FindGameObjectsWithTag("Player");

            for (i = 0; i < p.Length; i++)
            {
                id = p[i].GetComponent<PlayerID>().id;
                if (id == characterManager.index)
                {
                    break;
                }
            }

        }

       



        if (calculateDistance(p[i].transform.position) < 25)
        {
            passBall(id);
        }
    }

   

    void passBall(int index)
    {

        GameObject[] p = GameObject.FindGameObjectsWithTag("Player");

        GameObject player = p[0]; // Cojo un jugador (el primero de la lista)
        float minDistance = 1000000;
        float currentDistance;

        int id = p[0].GetComponent<PlayerID>().id; //Cojo su id
        int j; // Es la posición de la lista (no el id)
        int team = index % 2;


        for (int i = 0; i < p.Length; i++)
        {
            id = p[i].GetComponent<PlayerID>().id; //Veo si hay alguna con distancia menor 
            if (id != index && team == id % 2)
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

        OnCharacterGVSelected?.Invoke(player);



        Rigidbody rb = Bola.instance.GetComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;

        Vector3 direction = (player.transform.position - Bola.instance.transform.position).normalized;

         float passSpeed = 200f;
         rb.linearVelocity = direction * passSpeed;
         rb.angularVelocity = Vector3.zero;


    }

    float calculateDistance(Vector3 myPos)
    {
        Vector3 ballPosition = Bola.instance.transform.position;
        myPos.y = 0;
        ballPosition.y = 0;
        return Vector3.Distance(myPos, ballPosition); 
    }
}
