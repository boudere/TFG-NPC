using Unity.VisualScripting;
using UnityEngine;

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

    void Awake()
    {
        self = this;
        player = gameObject;
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

}

