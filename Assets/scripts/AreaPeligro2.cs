using System.Collections.Generic;
using UnityEngine;

public class AreaPeligro2 : MonoBehaviour
{

    List<PlayerID> playersInArea = new List<PlayerID>();
    [SerializeField] private Transform[] puntos = new Transform[4];
    private Dictionary<Delantero, Defensa> marcajes = new Dictionary<Delantero, Defensa>();
    private bool bolaArea = false;
    List<Defensa> defensas = new List<Defensa>();
    List<Delantero> delanterosSinMarcar = new List<Delantero>();

    public float minX;
    public float maxX;
    public float minZ;
    public float maxZ;


    public static AreaPeligro2 instance;

    private void Awake()
    {
        instance = this;

    }

    private void Update()
    {
        if (bolaArea)
        {
            //Saber si ese delantero tiene algún marcaje (desde la clase delantero) si no lo tiene se le añade
            foreach (Delantero d in delanterosSinMarcar)
            {
                LlamarDefensa(d);
                delanterosSinMarcar.Remove(d);
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
        if (jugador != null && !playersInArea.Contains(jugador))
            playersInArea.Add(jugador);

        if (other.CompareTag("Delantero") && Bola.instance.transform.IsChildOf(other.transform))
        {
            Delantero d = other.GetComponent<Delantero>();

            if (d != null)
                LlamarDefensa(d);
        } else if (other.CompareTag("Delantero") && !Bola.instance.transform.IsChildOf(other.transform))
        {
            Delantero d = other.GetComponent<Delantero>();
            delanterosSinMarcar.Add(d);
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
                delanterosSinMarcar.Add(d); 
            }

            return;
        }

        if (!other.CompareTag("Defensa") && !other.CompareTag("CentroCampista") && !other.CompareTag("Delantero"))
            return;

        PlayerID jugador = other.GetComponent<PlayerID>();
        if (jugador != null)
            playersInArea.Remove(jugador);

        if (other.CompareTag("Delantero"))
        {
            Delantero d = other.GetComponent<Delantero>();
            
            if (d == null) return;

            if (marcajes.TryGetValue(d, out Defensa def) && def != null)
            {
                def.QuitarMarcaje(d); 
            }
            marcajes.Remove(d);
        }
    }


    public int getPlayersInArea2()
    {
        return playersInArea.Count;
    }

    public int getPlayersInArea2ByTeam(int team)
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

    
}
