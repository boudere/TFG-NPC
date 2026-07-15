using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

public class WinTheBall : MonoBehaviour
{

   

    private GameObject[] p;
    public static CharacterManager characterManager;
    public static Bola ball;
    public static WinTheBall instance;
    public float distanciaMaxima;

    private void Awake()
    {
        instance = this;

    }

    void Start()
    {
        p = GetAllFieldPlayers();
        characterManager = CharacterManager.instance;
        ball = Bola.instance;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {

   
            for (int i = 0; i < p.Length; i++)
            {
                int id = p[i].GetComponent<PlayerID>().id;
                if (id == characterManager.index && ball != null && !Bola.instance.transform.IsChildOf(p[i].transform))
                {
                    
                    float distancia = Vector3.Distance(p[i].transform.position, ball.transform.position);
                    if (distancia < distanciaMaxima)
                    {
                     
                        PlayerID playerID = p[i].GetComponent<PlayerID>();
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
        }
        else if (Input.GetKeyDown(KeyCode.K))
        {
            Transform poseedorBalon = null;

            // Buscar quién tiene la pelota.
            for (int i = 0; i < p.Length; i++)
            {
                if (Bola.instance.transform.IsChildOf(p[i].transform))
                {
                    poseedorBalon = p[i].transform;
                    break;
                }
            }

            if (poseedorBalon == null)
            {
                return;
            }

         
            for (int i = 0; i < p.Length; i++)
            {
                PlayerID playerID = p[i].GetComponent<PlayerID>();

                if (playerID == null ||
                    playerID.id != characterManager.index)
                {
                    continue;
                }

              
                if (Bola.instance.transform.IsChildOf(p[i].transform))
                    return;

                float distancia = Vector3.Distance(
                    p[i].transform.position,
                    poseedorBalon.position
                );


                if (distancia <= 400f)
                {
                    GameObject player = playerID.player;
                    CharacterGV character = player.GetComponent<CharacterGV>();

                    if (character != null)
                    {
                        character.SetSprint(poseedorBalon.position);
                    }
                }

                break;
            }
        }
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