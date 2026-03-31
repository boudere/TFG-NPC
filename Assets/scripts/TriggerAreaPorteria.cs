using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TriggerAreaPorteria : MonoBehaviour
{

    private Porteria porteria;
    public int team;
    private PlayerID playerID;

    public float lineaGolMin;
    public float lineaGolMax;
    public float hx;

    public float velocidadMovimiento = 4f;
    public float velocidadPatrulla = 2f;

    private Portero porteroActivo;
    private bool defendiendo = false;
    private int direccion = 1;

    private Coroutine rutinaActual;

    List<int> playersInAreaId = new List<int>();

    void Start()
    {
        porteria = GetComponentInParent<Porteria>();
        team = porteria.team;
        lineaGolMax = porteria.lineaGolMax;
        lineaGolMin = porteria.lineaGolMin; 
        hx = porteria.hx;
    }

   

    void OnTriggerEnter(Collider other)
    {

        if (other.CompareTag("Defensa") || other.CompareTag("CentroCampista") || other.CompareTag("Delantero")) //PROVISIONAL
        {
            PlayerID jugador = other.GetComponent<PlayerID>();


            if (jugador == null) return;

            if (jugador != null && !playersInAreaId.Contains(jugador.id))
            {
                playersInAreaId.Add(jugador.id);
            }

            int teamJugador = jugador.id % 2;
            Bola bola = jugador.GetComponentInChildren<Bola>();

            // comprobamos si es del equipo contrario usando % 2
            if (bola != null && teamJugador % 2 != team)
            {
                //Debug.Log("TRIGGER ENTER con: " + jugador.name);

                porteroDefender();

            }

            if (other.CompareTag("Delantero"))
            {
                Delantero d = other.GetComponent<Delantero>();
                if (d != null)
                    llamarDelanteroDisparo(d);
            }
        }
    }

     void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Ball") && !Bola.instance.transform.IsChildOf(other.transform)) return;

        PlayerID jugador = other.GetComponent<PlayerID>();
        if (jugador != null && playersInAreaId.Contains(jugador.id))
        {
            playersInAreaId.Remove(jugador.id);
        }

        pararDefensa();
    }

    void llamarDelanteroDisparo(Delantero d)
    {
        d.activarTiro();
    }

    void porteroDefender()
    {
        if (porteroActivo != null) return; 

        GameObject[] porteros = GameObject.FindGameObjectsWithTag("Portero");

        foreach (var p in porteros)
        {
            Portero portero = p.GetComponent<Portero>();
            PlayerID pid = p.GetComponent<PlayerID>();

            if (portero != null && pid != null && pid.id % 2 == team)
            {
                porteroActivo = portero;
                porteroActivo.defendiendo = true;

                //Debug.Log("PORTERO DEFENDIENDO: " + p.name);
                break;
            }
        }
    }


    public void pararDefensa()
    {
        if (porteroActivo == null) return;

        //Debug.Log("PORTERO DESCANSA: ");

        porteroActivo.defendiendo = false;
        porteroActivo.PararDefensa();

        porteroActivo = null;
    }


    public bool isPlayerInSmallArea(int id)
    {
        return playersInAreaId.Contains(id);
    }

}
