using UnityEngine;

public class TriggerRobar : MonoBehaviour
{
    private PlayerID owner;

    void Awake()
    {
        owner = GetComponentInParent<PlayerID>();
    }

    private void OnTriggerEnter(Collider other)
    {
        //PlayerID otherPlayer = other.GetComponentInParent<PlayerID>();
        //if (otherPlayer == null) return;

        
        //if (otherPlayer == owner) return;

        
        
        
          
        //    Debug.Log("Robo");
        
    }
}
