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
    protected PlayerID playerStop = null;
    protected bool libre = true;
    protected float npcSpeed = 100f;
    protected Vector3 npcTarget;

    protected bool chasingBallFree = false;
    protected int areaConBola;
    protected float changeTargetDistance = 50f;

    float nextTiroBolaTime;
    float tiroBolaInterval = 1.5f;

    float renaudarMoveTime;
    float renaudarMove = 1f;

    protected string targetTag = "";
    public float minX, maxX;
    protected bool isOutside = false;

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

    public void whereIsBall()  // SABER DONDE ESTA LA BOLA
    {
        areaConBola = -1;
        GameObject[] areas = GameObject.FindGameObjectsWithTag("AreaPeligro");
        foreach (GameObject a in areas)
        {
            AreaPeligro area = a.GetComponent<AreaPeligro>();
            if (area.isBallInArea())
            {
                areaConBola = area.TEAM;
                break;
            }
        }
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

    public void Parar()
    {
        if (rb == null) return;


        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        stop = true;
    }

    public void changeSpeed() {
    
        if (Bola.instance.transform.IsChildOf(transform))
        {
            npcSpeed = 50f;
        }
        else
        {
            npcSpeed = 100f;
        }
    }

    public void Reanudar()
    {
        stop = false;
    }



    protected void goToOtherArea() 
    {

        if (Vector3.Distance(transform.position, npcTarget) > changeTargetDistance)
            return;

        GameObject[] areas = GameObject.FindGameObjectsWithTag("AreaPeligro");

        float x = transform.position.x;
        float z = transform.position.z;

        foreach (GameObject a in areas)
        {
            AreaPeligro area = a.GetComponent<AreaPeligro>();

            if (area != null && area.TEAM != this.id % 2) // �rea contraria
            {
                x = Random.Range(area.minX, area.maxX);
                z = Random.Range(area.minZ, area.maxZ);
                break;
            }
        }

        npcTarget = new Vector3(x, transform.position.y, z);
    }

    protected bool PuedoTirar()
    {
        if (Time.time < nextTiroBolaTime)
            return false;

        nextTiroBolaTime = Time.time + tiroBolaInterval;
        return true;
    }

    protected bool PuedoRenaudarMove()
    {
        if (Time.time < renaudarMoveTime)
            return false;

        renaudarMoveTime = Time.time + renaudarMove;
        return true;
    }

    protected void PaseConCriterio()
    {

        GameObject[] porterias = GameObject.FindGameObjectsWithTag("Porteria");
        foreach (GameObject p in porterias)
        {
            Porteria porteria = p.GetComponent<Porteria>();
            Transform breakGol = p.transform.Find("BreakGol");
            if (this.id % 2 != porteria.team % 2)
            {
                Pase.instance.PassBallNear(transform.position, breakGol.transform.position, this.id % 2, this);
                break;
            }
        }

    }

    protected IEnumerator StopRoutine(PlayerID playerStop)
    {
        Rigidbody rbPlayer = playerStop.GetComponent<Rigidbody>();
        rbPlayer.linearVelocity = Vector3.zero;
        rbPlayer.angularVelocity = Vector3.zero;
        yield return new WaitForSeconds(2f);
        frozen = false;
    }

}