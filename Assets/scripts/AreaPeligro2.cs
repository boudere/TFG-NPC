using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;
using UnityEngine;

public class AreaPeligro2 : MonoBehaviour
{

    List<PlayerID> playersInArea2 = new List<PlayerID>();
    [SerializeField] private Transform[] puntos = new Transform[4];
    private Dictionary<Delantero, Defensa> marcajes = new Dictionary<Delantero, Defensa>();
    private bool bolaArea = false;
    List<Defensa> defensas2 = new List<Defensa>();
    List<Delantero> delanterosSinMarcar2 = new List<Delantero>();
    List<CentroCampista> centros2 = new List<CentroCampista>();
    List<Delantero> delanteros2 = new List<Delantero>();

    public float minX;
    public float maxX;
    public float minZ;
    public float maxZ;

    public static int TEAM = 1; 

    public static AreaPeligro2 instance;

    private void Awake()
    {
        instance = this;

    }

    private void Update()
    {
        if (bolaArea)
        {
            for (int i = delanterosSinMarcar2.Count - 1; i >= 0; i--)
            {
                LlamarDefensa(delanterosSinMarcar2[i]);
                delanterosSinMarcar2.RemoveAt(i);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == other.CompareTag("Ball"))
        {
            bolaArea = true;
            return;
        }

        if (!other.CompareTag("Defensa") && !other.CompareTag("CentroCampista") && !other.CompareTag("Delantero"))
            return;

        PlayerID jugador = other.GetComponent<PlayerID>();
        if (jugador != null && !playersInArea2.Contains(jugador))
            playersInArea2.Add(jugador);

        if (other.CompareTag("Delantero") && Bola.instance.transform.IsChildOf(other.transform))
        {
            Delantero d = other.GetComponent<Delantero>();
            delanteros2.Add(d);

            if (d != null)
                LlamarDefensa(d);
        } else if (other.CompareTag("Delantero") && !Bola.instance.transform.IsChildOf(other.transform))
        {
            Delantero d = other.GetComponent<Delantero>();
            delanterosSinMarcar2.Add(d);
            delanteros2.Add(d);
        }

        if (other.CompareTag("CentroCampista"))
        {
            CentroCampista c = other.GetComponent<CentroCampista>();
            centros2.Add(c);
        }

        if (other.CompareTag("Defensa"))
        {
            Defensa def = other.GetComponent<Defensa>();
            defensas2.Add(def);
        }
    }

    private void LlamarDefensa(Delantero d)
    {
       
        if (marcajes.ContainsKey(d)) return;
        if (d.id % 2 != 0) return;

        GameObject[] players = GameObject.FindGameObjectsWithTag("Defensa");

        // Shuffle
        for (int i = players.Length - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);
            (players[i], players[randomIndex]) = (players[randomIndex], players[i]);
        }

        for (int i = 0; i < players.Length; i++)
        {
            Defensa def = players[i].GetComponent<Defensa>();
            if (def == null) continue;

           
            if (def.id % 2 == 1 && !def.defender)
            {
                def.AsignarMarcaje(d);     
                marcajes[d] = def;         
                break;
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Ball"))
        {
            bolaArea = false;
            GameObject[] delanteros = GameObject.FindGameObjectsWithTag("Delantero");
            foreach (GameObject del in delanteros)
            {
                Delantero d = del.GetComponent<Delantero>();
                if (d == null) return;

                if (marcajes.TryGetValue(d, out Defensa def) && def != null)
                {
                    def.QuitarMarcaje(d);
                }
                marcajes.Remove(d);
                delanterosSinMarcar2.Add(d); 
            }

            return;
        }

        if (!other.CompareTag("Defensa") && !other.CompareTag("CentroCampista") && !other.CompareTag("Delantero"))
            return;

        PlayerID jugador = other.GetComponent<PlayerID>();
        if (jugador != null)
            playersInArea2.Remove(jugador);

        if (other.CompareTag("Delantero"))
        {
            Delantero d = other.GetComponent<Delantero>();
            
            if (d == null) return;

            if (marcajes.TryGetValue(d, out Defensa def) && def != null)
            {
                def.QuitarMarcaje(d); 
            }
            marcajes.Remove(d);
            delanteros2.Remove(d);
        }


        if (other.CompareTag("CentroCampista"))
        {
            CentroCampista c = other.GetComponent<CentroCampista>();
            centros2.Remove(c);
        }

        if (other.CompareTag("Defensa"))
        {
            Defensa def = other.GetComponent<Defensa>();
            defensas2.Remove(def);
        }
    }


    public int getPlayersInArea2()
    {
        return playersInArea2.Count;
    }

    public List<Defensa> getDefensas2()
    {
        return defensas2;   
    }

    public List<CentroCampista> getCentroCampista2()
    {
        return centros2;
    }

    public List<Delantero> getDelanteros2()
    {
        return delanteros2;
    }

    public int getPlayersInArea2ByTeam(int team)
    {
        int team0 = 0, team1 = 0;

        for (int i = 0; i < playersInArea2.Count; i++)
        {
            PlayerID jugador = playersInArea2[i];
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

        for (int i = 0; i < defensas2.Count; i++)
        {
            Defensa jugador = defensas2[i];

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

        for (int i = 0; i < centros2.Count; i++)
        {
            CentroCampista jugador = centros2[i];

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

        for (int i = 0; i < delanteros2.Count; i++)
        {
            Delantero jugador = delanteros2[i];

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


}
