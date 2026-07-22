using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEngine.UI.GridLayoutGroup;


public class TriggerHavePlayer : MonoBehaviour
{

    private int id = -1;
    public static CharacterManager characterManager;
    private PlayerID owner;

    private bool locked = false;
    private bool robo = false;
    public static TriggerHavePlayer instance;
    private GameObject[] p;

    private void Awake()
    {
        instance = this;
    }
    void Start()
    {
        p = GetAllFieldPlayers();
        characterManager = CharacterManager.instance;
       
       
    }

    public void Update()
    {
        setRobo(NingunJugadorTienePelota());
    }


    public void setRobo (bool robo)
    {
        this.robo = robo;
    }

    public bool getRobo()
    {
        return this.robo;
    }


  

    private void OnTriggerEnter(Collider other)
    {
       

        owner = GetComponentInParent<PlayerID>();
        if (owner == null) return;




        PlayerID otherPlayer = other.GetComponentInParent<PlayerID>();
        if (otherPlayer != null)
        {
            if (otherPlayer.id % 2 == owner.id % 2 )
            {
                return;
            }


            Defensa defensa = otherPlayer.GetComponent<Defensa>();

            if (defensa != null && Bola.instance.transform.IsChildOf(owner.transform))
            {
               detectTag(owner.gameObject);
            }

        }

        if (locked) return;

        var rb = other.attachedRigidbody;
        if (rb == null) return;
        if (!rb.CompareTag("Ball")) return;

        if (!Bola.instance.PuedeSerRecogida(owner)) return;

        // Si la pelota ya la tiene el portero, ningún jugador de campo puede quitársela
        if (Bola.instance.EnPosesion && Bola.instance.Owner != null)
        {
            if (Bola.instance.Owner.CompareTag("Portero") || Bola.instance.Owner is Portero)
            {
                return;
            }
        }

        locked = true;
        Bola.instance.AsignarPosesion(owner);
        Arbitro.instance.BallEntraEnArea(owner);
       
    }

    public void detectTag(GameObject go)
    {
        if (go.CompareTag("CentroCampista"))
        {
            CentroCampista p = go.GetComponent<CentroCampista>();
            StartCoroutine(p.PararJugador(p));
        }
        else if (go.CompareTag("Defensa"))
        {
            Defensa p = go.GetComponent<Defensa>();
            StartCoroutine(p.PararJugador(p));
        }
        else if (go.CompareTag("Delantero"))
        {
            Delantero p = go.GetComponent<Delantero>();
            StartCoroutine(p.PararJugador(p));
        } else if (!go.CompareTag("Portero"))
        {
            CharacterGV p = go.GetComponent<CharacterGV>();
            StartCoroutine(p.PararJugador(p));
        }


    }

    public bool NingunJugadorTienePelota()
    {
       foreach (GameObject pl in p)
        {
            if (pl.GetComponent<PlayerID>().id == characterManager.index && Bola.instance.transform.IsChildOf(pl.transform))
            {
                return true;
            }

            if (Bola.instance.transform.IsChildOf(pl.transform))
            {
                return false;
            }
        }
        return true;
    }


    private void OnTriggerExit(Collider other)
    {
        if (other.isTrigger) return;
        var rb = other.attachedRigidbody;
        if (rb == null) return;
        if (!rb.CompareTag("Ball")) return;

        locked = false;

        if (owner != null)
        {
            Arbitro.instance.BallSaleDeArea(owner);
            owner = null;
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
