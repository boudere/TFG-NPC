using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

public class WinTheBall : MonoBehaviour
{

   

    private GameObject[] p;
    public static CharacterManager characterManager;
    public static TriggerHavePlayer havePlayer;
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
        havePlayer = TriggerHavePlayer.instance;
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
                    if (Bola.instance.EnPosesion && !EsPoseedorValidoParaRobar(Bola.instance.Owner))
                    {
                        break;
                    }

                    float distancia = Vector3.Distance(p[i].transform.position, ball.transform.position);
                    if (distancia < distanciaMaxima)
                    {
                        EjecutarRobo(p[i]);
                        break;
                    }
                }
            }
        }
        else if (Input.GetKeyDown(KeyCode.K))
        {
            Transform poseedorBalon = null;
            PlayerID poseedorPlayerID = null;

            // Buscar quién tiene la pelota.
            if (Bola.instance.EnPosesion && Bola.instance.Owner != null)
            {
                poseedorPlayerID = Bola.instance.Owner;
                poseedorBalon = poseedorPlayerID.transform;
            }
            else
            {
                for (int i = 0; i < p.Length; i++)
                {
                    if (Bola.instance.transform.IsChildOf(p[i].transform))
                    {
                        poseedorBalon = p[i].transform;
                        poseedorPlayerID = p[i].GetComponent<PlayerID>();
                        break;
                    }
                }
            }

            if (poseedorBalon == null || poseedorPlayerID == null)
            {
                return;
            }

            if (!EsPoseedorValidoParaRobar(poseedorPlayerID))
            {
                return;
            }

            for (int i = 0; i < p.Length; i++)
            {
                PlayerID playerID = p[i].GetComponent<PlayerID>();

                if (playerID == null || playerID.id != characterManager.index)
                {
                    continue;
                }

                if (Bola.instance.transform.IsChildOf(p[i].transform))
                    return;

                float distancia = Vector3.Distance(
                    p[i].transform.position,
                    poseedorBalon.position
                );

                if (distancia <= distanciaMaxima)
                {
                    EjecutarRobo(p[i]);
                }
                else if (distancia <= 400f)
                {
                    GameObject player = playerID.player;
                    CharacterGV character = player.GetComponent<CharacterGV>();

                    if (character != null)
                    {
                        character.SetSprintYRobar(poseedorBalon.position, playerID);
                    }
                }

                break;
            }
        }
    }

    public bool EsPoseedorValidoParaRobar(PlayerID owner)
    {
        if (owner == null) return true;
        if (owner.CompareTag("Portero") || owner is Portero) return false;
        if (owner.id % 2 == characterManager.index % 2) return false;
        return true;
    }

    public void EjecutarRobo(GameObject playerGo)
    {
        if (playerGo == null) return;
        if (Bola.instance.EnPosesion && !EsPoseedorValidoParaRobar(Bola.instance.Owner))
        {
            return;
        }

        havePlayer.setRobo(true);
        Bola.instance.Soltar();
        Rigidbody rb = Bola.instance.GetComponent<Rigidbody>();
        rb.isKinematic = false;
        Vector3 direction = (playerGo.transform.position - Bola.instance.transform.position).normalized;
        float passSpeed = 300f;
        rb.linearVelocity = direction * passSpeed;
        rb.angularVelocity = Vector3.zero;
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