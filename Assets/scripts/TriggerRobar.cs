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
        PlayerID otherPlayer = other.GetComponentInParent<PlayerID>();
        if (otherPlayer == null) return;

        // No te robes a ti mismo
        if (otherPlayer == owner) return;

        
        
        
            // Robo automático
            // Arbitro.instance.RobarBola(owner);
            Debug.Log("Robo");
        
    }
}
