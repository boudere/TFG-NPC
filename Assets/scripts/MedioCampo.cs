using System.Collections.Generic;
using UnityEngine;

public class MedioCampo : MonoBehaviour
{

    List<PlayerID> playersInArea = new List<PlayerID>();
    public static MedioCampo instance;

    List<Defensa> defensasM = new List<Defensa>();
    List<CentroCampista> centrosM = new List<CentroCampista>();
    List<Delantero> delanterosM = new List<Delantero>();

    private void Awake()
    {
        instance = this;

    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Defensa") && !other.CompareTag("CentroCampista") && !other.CompareTag("Delantero")) return;
        PlayerID jugador = other.GetComponent<PlayerID>();
        playersInArea.Add(jugador);

        if (other.CompareTag("Delantero"))
        {
            CentroCampista c = other.GetComponent<CentroCampista>();
            centrosM.Add(c);
        }

        if (other.CompareTag("CentroCampista"))
        {
            CentroCampista c = other.GetComponent<CentroCampista>();
            centrosM.Add(c);
        }

        if (other.CompareTag("Defensa"))
        {
            Defensa def = other.GetComponent<Defensa>();
            defensasM.Add(def);
        }

    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Defensa") && !other.CompareTag("CentroCampista") && !other.CompareTag("Delantero")) return;
        PlayerID jugador = other.GetComponent<PlayerID>();
        playersInArea.Remove(jugador);


        if (other.CompareTag("Delantero"))
        {
            CentroCampista c = other.GetComponent<CentroCampista>();
            centrosM.Remove(c);
        }

        if (other.CompareTag("CentroCampista"))
        {
            CentroCampista c = other.GetComponent<CentroCampista>();
            centrosM.Remove(c);
        }

        if (other.CompareTag("Defensa"))
        {
            Defensa def = other.GetComponent<Defensa>();
            defensasM.Remove(def);
        }

    }

    public int getPlayersInMedioCampo()
    {
        return playersInArea.Count;
    }

    public List<Defensa> getDefensasM()
    {
        return defensasM;
    }

    public List<CentroCampista> getCentroCampistaM()
    {
        return centrosM;
    }

    public List<Delantero> getDelanterosM()
    {
        return delanterosM;
    }

    public int getPlayersInMedioCampoByTeam(int team)
    {
        int team0 = 0, team1 = 0;

        for (int i = 0; i < playersInArea.Count; i++)
        {
            PlayerID jugador = playersInArea[i];
            if (jugador.id % 2 == 0)
            {
                team0++;
            }
            else if (jugador.id % 2 == 1)
            {
                team1++;
            }
        }

        if (team == 0)
        {
            return team0;
        }
        else
        {
            return team1;
        }
    }

    public int getDefensas1ByTeam(int team)
    {
        int team0 = 0, team1 = 0;

        for (int i = 0; i < defensasM.Count; i++)
        {
            Defensa jugador = defensasM[i];

            if (jugador.id % 2 == 0)
            {
                team0++;
            }
            else
            {
                team1++;
            }
        }

        return team == 0 ? team0 : team1;
    }

    public int getCentros1ByTeam(int team)
    {
        int team0 = 0, team1 = 0;

        for (int i = 0; i < centrosM.Count; i++)
        {
            CentroCampista jugador = centrosM[i];

            if (jugador.id % 2 == 0)
            {
                team0++;
            }
            else
            {
                team1++;
            }
        }

        return team == 0 ? team0 : team1;
    }

    public int getDelanteros1ByTeam(int team)
    {
        int team0 = 0, team1 = 0;

        for (int i = 0; i < delanterosM.Count; i++)
        {
            Delantero jugador = delanterosM[i];

            if (jugador.id % 2 == 0)
            {
                team0++;
            }
            else
            {
                team1++;
            }
        }

        return team == 0 ? team0 : team1;
    }

    public bool IsPlayerInArea(PlayerID jugador)
    {
        return jugador != null && playersInArea.Contains(jugador);
    }
}
