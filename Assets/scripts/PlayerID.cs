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
}