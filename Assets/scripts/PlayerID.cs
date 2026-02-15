using Unity.VisualScripting;
using UnityEngine;

public class PlayerID : MonoBehaviour
{
    public int id;
    public bool haveBall = false;
    public Transform holdPoint;
    public PlayerID self;

    void Awake()
    {
        self = this;
    }

}

