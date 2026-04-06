using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

public class AreaPeligro : MonoBehaviour { 



    List<PlayerID> playersInArea = new List<PlayerID>();
    List<int> playersInAreaId = new List<int>();
[SerializeField] private Transform[] puntos = new Transform[4];
private Dictionary<Delantero, Defensa> marcajes = new Dictionary<Delantero, Defensa>();

public float minX;
public float maxX;
public float minZ;
public float maxZ;
public static AreaPeligro instance;

private bool bolaArea = false;

List<Delantero> delanterosSinMarcar = new List<Delantero>();


List<CentroCampista> centros = new List<CentroCampista>();
List<Delantero> delanteros = new List<Delantero>();
List<Defensa> defensas = new List<Defensa>();

public int TEAM;



    private void Update()
    {
        if (bolaArea)
        {
            for (int i = delanterosSinMarcar.Count - 1; i >= 0; i--)
            {
                LlamarDefensa(delanterosSinMarcar[i]);
                delanterosSinMarcar.RemoveAt(i);
            }

        }
    }

    private void OnTriggerEnter(Collider other)
    {


        if (other.CompareTag("Ball"))
        {
            //Debug.Log("AAA");
            bolaArea = true;
            return;
        }

        if (Bola.instance.transform.IsChildOf(other.transform))
        {
            //Debug.Log("BBB");
            bolaArea = true;
        }

        if (!other.CompareTag("Defensa") && !other.CompareTag("CentroCampista") && !other.CompareTag("Delantero"))
            return;

        PlayerID jugador = other.GetComponent<PlayerID>();
        if (jugador != null && !playersInArea.Contains(jugador))
        {
            playersInArea.Add(jugador);
            playersInAreaId.Add(jugador.id);
        }
           

        if ((other.CompareTag("Delantero") && Bola.instance.transform.IsChildOf(other.transform)) || (other.CompareTag("Delantero") && bolaArea))
        {
            //bolaArea = true;
            Delantero d = other.GetComponent<Delantero>();
            delanteros.Add(d);

            if (d != null)
                LlamarDefensa(d);

        }
        else if (other.CompareTag("Delantero") && !Bola.instance.transform.IsChildOf(other.transform))
        {
            Delantero d = other.GetComponent<Delantero>();
            if (!delanterosSinMarcar.Contains(d))
            {
                delanterosSinMarcar.Add(d);
            }
            delanteros.Add(d);
        }

        if (other.CompareTag("CentroCampista"))
        {
            CentroCampista c = other.GetComponent<CentroCampista>();
            centros.Add(c);
        }

        if (other.CompareTag("Defensa"))
        {
            Defensa def = other.GetComponent<Defensa>();
            defensas.Add(def);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Ball"))
        {
            //Debug.Log("CCC");
            bolaArea = false;

  

            foreach (Delantero d in delanteros)
            {
                if (d == null) return;

                if (marcajes.TryGetValue(d, out Defensa def) && def != null)
                {
                    def.QuitarMarcaje(d);
                }
                marcajes.Remove(d);
                if (!delanterosSinMarcar.Contains(d))
                {
                    delanterosSinMarcar.Add(d);
                }
            }

            return;
        }

        if (Bola.instance.transform.IsChildOf(other.transform))
        {
            //Debug.Log("DDD");
            bolaArea = false;
          
        }


        if (!other.CompareTag("Defensa") && !other.CompareTag("CentroCampista") && !other.CompareTag("Delantero"))
            return;

        PlayerID jugador = other.GetComponent<PlayerID>();
        if (jugador != null && playersInArea.Contains(jugador))
        {
            playersInArea.Remove(jugador);
            playersInAreaId.Remove(jugador.id);
        }
           

        if (other.CompareTag("Delantero"))
        {
            Delantero d = other.GetComponent<Delantero>();
            if (d == null) return;

            if (marcajes.TryGetValue(d, out Defensa def) && def != null)
            {
                def.QuitarMarcaje(d);
            }
            marcajes.Remove(d);
            delanteros.Remove(d);
            delanterosSinMarcar.Remove(d);
        }

        if (other.CompareTag("CentroCampista"))
        {
            CentroCampista c = other.GetComponent<CentroCampista>();
            centros.Remove(c);
        }

        if (other.CompareTag("Defensa"))
        {
            Defensa def = other.GetComponent<Defensa>();
            defensas.Remove(def);
        }


    }

    private void LlamarDefensa(Delantero d)
    {
        if (marcajes.ContainsKey(d)) return;
        if (d.id % 2 == TEAM) return;

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


            if (def.id % 2 == TEAM && !def.defender)
            {
                def.AsignarMarcaje(d);
                marcajes[d] = def;
                break;
            }
        }
    }

    public int getPlayersInArea()
    {
        return playersInArea.Count;
    }

    public List<Defensa> getDefensas()
    {
        return defensas;
    }

    public List<CentroCampista> getCentroCampista()
    {
        return centros;
    }

    public List<Delantero> getDelanteros()
    {
        return delanteros;
    }


    public int getPlayersByTeam(int team)
    {
        int team0 = 0, team1 = 0;

        for (int i = 0; i < playersInArea.Count; i++)
        {
            PlayerID jugador = playersInArea[i];

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

    public bool isBallInArea()
    {
        return bolaArea;
    }

    public bool isPlayerInArea(int id)
    {
        return playersInAreaId.Contains(id);
    }
}
