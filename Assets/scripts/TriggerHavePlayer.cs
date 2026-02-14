using System;
using UnityEngine;
using static UnityEngine.UI.GridLayoutGroup;


public class TriggerHavePlayer : MonoBehaviour
{

    private int id = -1;
    public static CharacterManager characterManager;
    private PlayerID owner;



    private void OnTriggerEnter(Collider other)
    {
        owner = GetComponentInParent<PlayerID>();

        if (owner == null) return;
        if (!other.CompareTag("Ball")) return;

      

        if (!Bola.instance.PuedeSerRecogida()) return;

        Bola.instance.AsignarPosesion(owner);
        

   
            id = owner.id;
            Arbitro.instance.BallEntraEnArea(owner);

       

    }

    private void OnTriggerExit(Collider other)
    {

        if (!other.CompareTag("Ball")) return;

        

        if (owner != null)
        {
            Arbitro.instance.BallSaleDeArea(owner);
            id = -1;
            owner = null;
        }
    }


}
