using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Bola : MonoBehaviour
{
    public static Bola instance;
    public List<PlayerID> jugadores = new List<PlayerID>();
    public List<PlayerID> jugadoresOrdenados = new List<PlayerID>();


    [Header("Refs")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Collider physicalCollider;      // NO trigger
   // [SerializeField] private Collider stealTriggerCollider;  // SÍ trigger

    [Header("Posesión")]
    [SerializeField] private Vector3 localHoldOffset = new Vector3(0f, 0f, 1f);

 
   
    public bool EnPosesion { get; private set; }
    public PlayerID Owner { get; private set; }
    private Collider[] ownerColliders;

    private float blockPickupUntil = 0f;
    private CharacterManager characterManager;

    [SerializeField] private float refreshInterval = 0.15f;
    private float nextRefreshTime;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (rb == null)
            rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        characterManager = CharacterManager.instance;
        InicializarJugadores();
    }

    private void Update()
    {
        if (Time.time < nextRefreshTime)
            return;

        nextRefreshTime = Time.time + refreshInterval;

        actualizarPerseguidores();
    }

    private void InicializarJugadores()
    {
        jugadores.Clear();

        GameObject[] allPlayers = GetAllFieldPlayers();

        foreach (GameObject go in allPlayers)
        {
            if (go == null) continue;

            PlayerID p = go.GetComponent<PlayerID>();
            if (p == null) continue;

            jugadores.Add(p);
        }
    }

    void actualizarPerseguidores()
    {
      
        OrdenarJugadoresPorDistancia();
        

        if (!EnPosesion)
        {
            bool e0 = false;
            bool e1 = false;

            foreach (PlayerID p in jugadoresOrdenados)
            {
                if (p.id == characterManager.index) continue;
                if (e0 && e1) break;
                if (!p.getLibre()) continue;

                if (p.id % 2 == 0 && !e0)
                {
                  
                    p.setChasingBall(true);
                    e0 = true;
                }
                else if (p.id % 2 == 1 && !e1)
                {
                    p.setChasingBall(true);
                    e1 = true;
                }
            }
        }
        else
        {
            foreach (PlayerID p in jugadores)
                p.setChasingBall(false);
        }
    }

    void OrdenarJugadoresPorDistancia()
    {
        jugadoresOrdenados.Clear();
        jugadoresOrdenados.AddRange(jugadores);

        jugadoresOrdenados.Sort((a, b) =>
        {
            float da = (a.transform.position - transform.position).sqrMagnitude;
            float db = (b.transform.position - transform.position).sqrMagnitude;
            return da.CompareTo(db);
        });
    }

    public void RegistrarJugador(PlayerID jugador)
    {
        if (jugador == null) return;

        if (!jugador.CompareTag("Defensa") &&
            !jugador.CompareTag("CentroCampista") &&
            !jugador.CompareTag("Delantero"))
            return;

        if (!jugadores.Contains(jugador))
            jugadores.Add(jugador);
    }

    public void DesregistrarJugador(PlayerID jugador)
    {
        jugadores.Remove(jugador);
    }


    public bool PuedeSerRecogida()
    {
        return Time.time >= blockPickupUntil;
    }

   


    


    public void AsignarPosesion(PlayerID newOwner)
    {
    

        // Si ya la tiene este jugador, no rehagas todo
        if (EnPosesion && Owner == newOwner) return;
        
        if (EnPosesion && Owner.CompareTag("Portero")) return;
        
      

        // Si venía con otro dueño, restaurar colisiones
        if (Owner != null) RestaurarColisionesConOwner();

        Owner = newOwner;
        EnPosesion = true;
        // Cambiar a layer de bola en posesión
        gameObject.layer = LayerMask.NameToLayer("BallHeld");

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;

        // Asegurar estados
        physicalCollider.enabled = false;
        //stealTriggerCollider.enabled = true;
        //stealTriggerCollider.isTrigger = true;


        // Mantener escala mundial del balón
        Vector3 worldScale = transform.lossyScale;

        transform.SetParent(newOwner.transform, false);
        transform.localPosition = localHoldOffset;
        transform.localRotation = Quaternion.identity;


        Vector3 parentScale = transform.parent.lossyScale;
        float sx = Mathf.Abs(parentScale.x) < 0.000001f ? 1f : parentScale.x;
        float sy = Mathf.Abs(parentScale.y) < 0.000001f ? 1f : parentScale.y;
        float sz = Mathf.Abs(parentScale.z) < 0.000001f ? 1f : parentScale.z;

        transform.localScale = new Vector3(worldScale.x / sx, worldScale.y / sy, worldScale.z / sz);



    }

    private void RestaurarColisionesConOwner()
    {
        if (ownerColliders == null) return;

        foreach (var c in ownerColliders)
        {
            if (c == null) continue;
            Physics.IgnoreCollision(physicalCollider, c, false);
            //Physics.IgnoreCollision(stealTriggerCollider, c, false);
        }

        ownerColliders = null;
    }
    public void Soltar()
    {
        gameObject.layer = LayerMask.NameToLayer("Default");

        RestaurarColisionesConOwner();

        EnPosesion = false;
        Owner = null;

        transform.SetParent(null, true);

        physicalCollider.enabled = true;

        rb.isKinematic = false;
        rb.useGravity = true;

     
        blockPickupUntil = Time.time + 0.25f;
        rb.WakeUp();
    }


    private GameObject[] GetAllFieldPlayers()
    {
        List<GameObject> allPlayers = new List<GameObject>();

        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("Defensa"));
        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("CentroCampista"));
        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("Delantero"));

        return allPlayers.ToArray();
    }

    public List<PlayerID> getJugadoresOrdenados()
    {
        return jugadoresOrdenados;
    }
}