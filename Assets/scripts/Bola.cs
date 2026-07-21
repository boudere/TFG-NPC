using System;
using System.Collections;
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


    [SerializeField] private float pickupCooldown = 0.25f;
    private Dictionary<int, float> blockedPickupByPlayerId = new Dictionary<int, float>();

    private CharacterManager characterManager;

    private float refreshInterval = 1;
    private float nextRefreshTime;

    public bool ballIsOutside = false;

    private bool resettingBall = false;

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
        if (resettingBall)
            return;

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

        foreach (PlayerID p in jugadores)
        {
            detectTag(p.gameObject, false);

        }

      
        if (EnPosesion)
            return;

        OrdenarJugadoresPorDistancia();

        bool e0 = false;
        bool e1 = false;

        foreach (PlayerID p in jugadoresOrdenados)
        {
            if (p.id == characterManager.index) continue;
            if (e0 && e1) break;
            if (!p.getLibre()) continue;

            if (p.id % 2 == 0 && !e0)
            {
               
                detectTag(p.gameObject, true);

                e0 = true;
            }
            else if (p.id % 2 == 1 && !e1)
            {
                detectTag(p.gameObject, true);
                e1 = true;
            }
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


    public bool PuedeSerRecogida(PlayerID player)
    {
        if (player == null) return false;

        if (!blockedPickupByPlayerId.TryGetValue(player.id, out float blockedUntil))
            return true;

        if (Time.time >= blockedUntil)
        {
            blockedPickupByPlayerId.Remove(player.id);
            return true;
        }

        return false;
    }


    private void BloquearRecogidaParaJugador(PlayerID player)
    {
        if (player == null) return;

        blockedPickupByPlayerId[player.id] = Time.time + pickupCooldown;
    }



    public void detectTag(GameObject go, bool value)
    {
        if (go.CompareTag("CentroCampista"))
        {
            CentroCampista p = go.GetComponent<CentroCampista>();
            p.setChasingBallFree(value, p);
        }
        else if (go.CompareTag("Defensa"))
        {
            Defensa p = go.GetComponent<Defensa>();
            p.setChasingBallFree(value, p);
        }
        else if (go.CompareTag("Delantero"))
        {
            Delantero p = go.GetComponent<Delantero>();
            p.setChasingBallFree(value, p);
        }

       
    }

    public void AsignarPosesion(PlayerID newOwner)
    {


        // Si ya la tiene este jugador, no rehagas todo

        if (newOwner.id == characterManager.index && !TriggerHavePlayer.instance.getRobo())
        {
          
            return;

        }

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

        TriggerHavePlayer.instance.setRobo(false);

    }

    private void RestaurarColisionesConOwner()
    {
        if (ownerColliders == null) return;
        foreach (PlayerID p in jugadores)
        {
            p.setChasingBallFree(false, p);
        }

        foreach (var c in ownerColliders)
        {
            if (c == null) continue;
            Physics.IgnoreCollision(physicalCollider, c, false);
        }

        ownerColliders = null;
    }


    public void Soltar()
    {
        PlayerID previousOwner = Owner;

        gameObject.layer = LayerMask.NameToLayer("Default");

        RestaurarColisionesConOwner();

        EnPosesion = false;
        Owner = null;

        transform.SetParent(null, true);

        physicalCollider.enabled = true;

        rb.isKinematic = false;
        rb.useGravity = true;

        BloquearRecogidaParaJugador(previousOwner);
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

    public IEnumerator resetBall()
    {
        yield return new WaitForSeconds(4f);
    }

    public Rigidbody GetRigidbody()
    {
        return rb;
    }


}