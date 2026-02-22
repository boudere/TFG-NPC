using System.Collections.Generic;
using UnityEngine;

public class AreaPeligro1 : MonoBehaviour
{

    List<PlayerID> playersInArea = new List<PlayerID>(); 


    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Defensa") && !other.CompareTag("CentroCampista") && !other.CompareTag("Delantero")) return;
       PlayerID jugador = other.GetComponent<PlayerID>();
        playersInArea.Add(jugador);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Defensa") && !other.CompareTag("CentroCampista") && !other.CompareTag("Delantero")) return;
        PlayerID jugador = other.GetComponent<PlayerID>();
        playersInArea.Remove(jugador);

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
