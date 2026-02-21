using System;
using UnityEngine;
using static UnityEngine.UI.GridLayoutGroup;


public class TriggerHavePlayer : MonoBehaviour
{

    private int id = -1;
    public static CharacterManager characterManager;
    private PlayerID owner;

    private bool locked = false;

    private void OnTriggerEnter(Collider other)
    {
        if (locked) return;

        owner = GetComponentInParent<PlayerID>();
        if (owner == null) return;

        var rb = other.attachedRigidbody;
        if (rb == null) return;
        if (!rb.CompareTag("Ball")) return;

        if (!Bola.instance.PuedeSerRecogida()) return;

        locked = true;
        Debug.Log($"TriggerHavePlayer ENTER -> this={name} id={GetInstanceID()} root={transform.root.name} other={other.name}");
        Bola.instance.AsignarPosesion(owner);
        Arbitro.instance.BallEntraEnArea(owner);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.isTrigger) return;
        var rb = other.attachedRigidbody;
        if (rb == null) return;
        if (!rb.CompareTag("Ball")) return;

        locked = false;

        if (owner != null)
        {
            Arbitro.instance.BallSaleDeArea(owner);
            owner = null;
        }
    }

}
