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
       

        owner = GetComponentInParent<PlayerID>();
        if (owner == null) return;

        PlayerID otherPlayer = other.GetComponentInParent<PlayerID>();
        if (otherPlayer != null)
        {
            if (otherPlayer.id % 2 == owner.id % 2)
            {
                return;
            }



            Defensa defensa = otherPlayer.GetComponent<Defensa>();

            if (defensa != null)
            {
                StartCoroutine(owner.PararJugador(owner.gameObject));
            }

        }

        if (locked) return;

        var rb = other.attachedRigidbody;
        if (rb == null) return;
        if (!rb.CompareTag("Ball")) return;

        if (!Bola.instance.PuedeSerRecogida()) return;

        locked = true;
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
