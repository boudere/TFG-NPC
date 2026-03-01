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

    void Awake()
    {
        self = this;
        player = gameObject;
    }

}

