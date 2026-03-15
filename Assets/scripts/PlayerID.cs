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

    void Awake()
    {
        self = this;
        player = gameObject;
        rb = GetComponent<Rigidbody>();
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


    public IEnumerator PararJugador(GameObject target)
    {
        frozen = true;
        playerStop = target;

        Rigidbody rbPlayer = playerStop.GetComponent<Rigidbody>();
        rbPlayer.linearVelocity = Vector3.zero;
        rbPlayer.angularVelocity = Vector3.zero;

        yield return new WaitForSeconds(2f);

        frozen = false;
        playerStop = null;
    }
}

