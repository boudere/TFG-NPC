using Unity.VisualScripting;
using UnityEngine;
using System.Collections;

public class PlayerID : MonoBehaviour
{
    public int id;
    public string posicion;
    public bool haveBall = false;
    public Transform holdPoint;
    public PlayerID self;
    public float probabilidadAciertoPase;
    public GameObject player;
    private float secondTimer = 0f;
    protected Rigidbody rb;
    protected bool stop = false;
    protected bool frozen = false;
    protected GameObject playerStop = null;
    protected bool libre = true;
    protected float npcSpeed = 75;
    protected bool chasingBall  = false;

    void Awake()
    {
        self = this;
        player = gameObject;
        rb = GetComponent<Rigidbody>();
        Bola.instance.RegistrarJugador(this);
    }


    protected bool Every(float interval)
    {
        secondTimer += Time.deltaTime;

        if (secondTimer >= interval)
        {
            secondTimer -= interval;
            return true;
        }

        return false;
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

    public bool getChasingBall()
    {
        return chasingBall;
    }

    public void setChasingBall(bool chasingBall)
    {
        this.chasingBall = chasingBall;
    }



    protected void runToBall()
    {
        Vector3 dir = Bola.instance.transform.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.001f) return;

        Vector3 velocity = dir.normalized * npcSpeed;
        rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);

        Quaternion rot = Quaternion.LookRotation(dir);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, rot, 10f * Time.deltaTime));
    }

    public IEnumerator PararJugador(GameObject target)
    {
        stop = true;
        playerStop = target;

        Rigidbody rbPlayer = playerStop.GetComponent<Rigidbody>();
        rbPlayer.linearVelocity = Vector3.zero;
        rbPlayer.angularVelocity = Vector3.zero;

        yield return new WaitForSeconds(2f);

        stop = false;
        playerStop = null;
    }
}

