using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public class Pase : MonoBehaviour
{

    private GameObject[] players;
    public Bola ball;
    public static CharacterManager characterManager;
    public static CharacterGV characterGV;
    private NavMeshAgent navMeshAgent;
    public static event Action <GameObject> OnCharacterGVSelected;


   
    void Start()
    {
        characterManager = CharacterManager.instance;
    }

    
    void Update()
    {

            if (Input.GetKeyDown(KeyCode.P))
        {

            Bola.instance.Soltar();

            GameObject[] p = GameObject.FindGameObjectsWithTag("Player");
            int id, i;

            for ( i = 0; i < p.Length; i++)
            {
                id = p[i].GetComponent<PlayerID>().id;
                if (id == characterManager.index)
                {
                    break;
                }
            }

            if (calculateDistance(p[i].transform.position) < 25)
            {
                passBall(characterManager.index, p);
            }
        }
        
    }

    void passBall(int index, GameObject[] p)
    {

        GameObject player = p[0]; // Cojo un jugador (el primero de la lista)
        float minDistance;
        float currentDistance;

        int id = p[0].GetComponent<PlayerID>().id; //Cojo su id
        int j; // Es la posición de la lista (no el id)
       

        if (index != id) // Comparo si es el mismo que MI JUGADOR
        {                //Si no es el mismo, continuo
            minDistance = calculateDistance(p[0].transform.position); //Calculo la distancia a la bola
            currentDistance = calculateDistance(p[0].transform.position);
            j = 0;
        } else //Si fuese la misma, cojo el siguiente 
        {
            minDistance = calculateDistance(p[1].transform.position);
            currentDistance = calculateDistance(p[1].transform.position);
            j = 1;
        }


        for (int i = 0; i < p.Length; i++)
        {
            id = p[i].GetComponent<PlayerID>().id; //Veo si hay alguna con distancia menor 
            if (id != index)
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
