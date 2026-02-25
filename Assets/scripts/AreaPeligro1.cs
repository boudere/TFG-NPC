using System.Collections.Generic;
using UnityEngine;

public class AreaPeligro1 : MonoBehaviour
{

    List<PlayerID> playersInArea = new List<PlayerID>();
    [SerializeField] private Transform[] puntos = new Transform[4];
    public float minX;
    public float maxX;
    public float minZ;
    public float maxZ;
    public static AreaPeligro1 instance;

    private void Awake()
    {
        instance = this;

    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Defensa") && !other.CompareTag("CentroCampista") && !other.CompareTag("Delantero")) return;

        if (other.CompareTag("Delantero"))
        {
            llamarDefensa(other);
        }

        PlayerID jugador = other.GetComponent<PlayerID>();
        playersInArea.Add(jugador);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Defensa") && !other.CompareTag("CentroCampista") && !other.CompareTag("Delantero")) return;
        PlayerID jugador = other.GetComponent<PlayerID>();
        playersInArea.Remove(jugador);

    }

    private void llamarDefensa(Collider other) //Llamo a defender 
    {
        Delantero d = other.GetComponent<Delantero>();
        if (d.id % 2 == 0)
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag("Defensa");

            for (int i = players.Length - 1; i > 0; i--)
            {
                int randomIndex = Random.Range(0, i + 1);
                GameObject temp = players[i];
                players[i] = players[randomIndex];
                players[randomIndex] = temp;
            }  //Mezcla xd


            for (int i = 0; i < players.Length; i++)
            {
                Defensa def = players[i].GetComponent<Defensa>();

                if (def.id % 2 == 1 && !def.defender)
                {
                    def.activarDefensa(d.transform);
                }

            }

        }
    }

    public int getPlayersInArea1()
    {
        return playersInArea.Count;
    }


    public int getPlayersInArea1ByTeam(int team)
    {
        int team0 = 0, team1 = 0;

        for (int i = 0; i < playersInArea.Count; i++)
        {
            PlayerID jugador = playersInArea[i];
            if (jugador.id % 2 == 0)
            {
                team0++;
            } else if (jugador.id % 2 == 1)
            {
                team1++;
            }
        }

        if (team == 0)
        {
            return team0;
        } else
        {
            return team1;
        }
    }

}



