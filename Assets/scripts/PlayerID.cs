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

    void Awake()
    {
        self = this;
    }

}

