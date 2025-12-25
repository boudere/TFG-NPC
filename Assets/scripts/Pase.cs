using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public class Pase : MonoBehaviour
{

    private GameObject[] players;
    public GameObject ball;
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

        GameObject player = p[0];
        float minDistance;
        float currentDistance;

        int id = p[0].GetComponent<PlayerID>().id;
        int j;

        if (index != id)
        {
            minDistance = calculateDistance(p[0].transform.position);
            currentDistance = calculateDistance(p[0].transform.position);
            j = 0;
        } else
        {
            minDistance = calculateDistance(p[1].transform.position);
            currentDistance = calculateDistance(p[1].transform.position);
            j = 1;
        }


        for (int i = 0; i < p.Length; i++)
        {
            id = p[i].GetComponent<PlayerID>().id;
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

        //CharacterGV playerStop = CharacterManager.instance.characterList[id].characterGV;
        //Debug.Log(CharacterManager.instance.characters.Length);
        //  CharacterGV playerStop = CharacterManager.instance.characters[id];
        // playerStop.stopPlayerPass();

        OnCharacterGVSelected?.Invoke(player);



        Rigidbody rb = ball.GetComponent<Rigidbody>();

         Vector3 direction = (player.transform.position - ball.transform.position).normalized;

         float passSpeed = 200f;
         rb.linearVelocity = direction * passSpeed;
         rb.angularVelocity = Vector3.zero;


    }

    float calculateDistance(Vector3 myPos)
    {
        Vector3 ballPosition = ball.transform.position;
        myPos.y = 0;
        ballPosition.y = 0;

        return Vector3.Distance(myPos, ballPosition); 
    }
}
