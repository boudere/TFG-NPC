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
        if (!other.CompareTag("Ball")) return;

        
        owner = GetComponentInParent<PlayerID>();
       
        if (owner != null)
        {
            id = owner.id;
            Arbitro.instance.BallEntraEnArea(owner);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        

        if (owner != null)
        {
            Arbitro.instance.BallSaleDeArea(owner);
            id = -1;
            owner = null;
        }
    }


}
