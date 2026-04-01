using System.Collections;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;

public class PlayerID : MonoBehaviour
{
    public int id;
    public string posicion;
    public bool haveBall = false;
    public Transform holdPoint;
    public float probabilidadAciertoPase;
    public GameObject player;

    protected Rigidbody rb;
    protected bool stop = false;
    protected bool frozen = false;
    protected GameObject playerStop = null;
    protected bool libre = true;
    protected float npcSpeed = 75f;
    protected Vector3 npcTarget;

    protected bool chasingBallFree = false;

    protected virtual void Awake()
    {
        player = gameObject;
        rb = GetComponent<Rigidbody>();
    }

    public float DistanciaBola()
    {
        return Vector3.Distance(transform.position, Bola.instance.transform.position);
    }

    public bool getLibre()
    {
        return libre;
    }

    public void setLibre(bool libre)
    {
        this.libre = libre;
    }

    public bool getChasingBallFree(PlayerID p)
    {
        return chasingBallFree;
    }

    public void setChasingBallFree(bool value, PlayerID p)
    {
        chasingBallFree = value;
    }

    public IEnumerator PararJugador(GameObject target)
    {
        stop = true;
        playerStop = target;

        Rigidbody rbPlayer = playerStop.GetComponent<Rigidbody>();
        if (rbPlayer != null)
        {
            rbPlayer.linearVelocity = Vector3.zero;
            rbPlayer.angularVelocity = Vector3.zero;
        }

        yield return new WaitForSeconds(2f);

        stop = false;
        playerStop = null;
    }

    public int whereIsPlayer()
    { 
        GameObject[] areas = GameObject.FindGameObjectsWithTag("AreaPeligro");
        foreach (GameObject a in areas)
        {
            AreaPeligro area = a.GetComponent<AreaPeligro>();
            if (area.isPlayerInArea(this.id))
            {
                return area.TEAM;

            }
        }

        return -1;
    }

    public int IsInSmallArea()
    {
        TriggerAreaPorteria[] areas = GetComponentsInChildren<TriggerAreaPorteria>();

        foreach (TriggerAreaPorteria area in areas)
        {
            if (area.isPlayerInSmallArea(this.id))
            {
                return area.team;
            }
        }

        return -1;
    }

    public void tiroPorteria()
    {
        GameObject[] porterias = GameObject.FindGameObjectsWithTag("Porteria");
        Vector3 dir = new Vector3(0, 0);

        foreach (GameObject p in porterias)
        {
            Porteria porteria = p.GetComponent<Porteria>();
            Transform breakGol = p.transform.Find("BreakGol");

            if (this.id % 2 != porteria.team % 2)
            {
                npcTarget = new Vector3(
                    breakGol.position.x,
                    transform.position.y,
                    breakGol.position.z
                );

                dir = npcTarget - transform.position;
                break;
            }
        }

        dir.y = 0f;
        if (dir != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(dir);
        }

        Vector3 velocity = dir.normalized * npcSpeed;
        rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);

        Shoot.instance.disparoLibre();
    }


}