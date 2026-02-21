using System;
using UnityEngine;

public class Bola : MonoBehaviour
{
    public static Bola instance;

    [Header("Refs")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Collider physicalCollider;      // NO trigger
   // [SerializeField] private Collider stealTriggerCollider;  // SÍ trigger

    [Header("Posesión")]
    [SerializeField] private Vector3 localHoldOffset = new Vector3(0f, 0f, 1f);

 
   
    public bool EnPosesion { get; private set; }
    public PlayerID Owner { get; private set; }
    private Collider[] ownerColliders;

    private float blockPickupUntil = 0f;

    public bool PuedeSerRecogida()
    {
        return Time.time >= blockPickupUntil;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (rb == null)
            rb = GetComponent<Rigidbody>();
    }


    public void AsignarPosesion(PlayerID newOwner)
    {
    

        // Si ya la tiene este jugador, no rehagas todo
        if (EnPosesion && Owner == newOwner) return;
        
        if (EnPosesion && Owner.CompareTag("Portero")) return;
        
      

        // Si venía con otro dueño, restaurar colisiones
        if (Owner != null) RestaurarColisionesConOwner();

        Owner = newOwner;
        EnPosesion = true;
        // Cambiar a layer de bola en posesión
        gameObject.layer = LayerMask.NameToLayer("BallHeld");

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;

        // Asegurar estados
        physicalCollider.enabled = false;
        //stealTriggerCollider.enabled = true;
        //stealTriggerCollider.isTrigger = true;


        // Mantener escala mundial del balón
        Vector3 worldScale = transform.lossyScale;

        transform.SetParent(newOwner.transform, false);
        transform.localPosition = localHoldOffset;
        transform.localRotation = Quaternion.identity;


        Vector3 parentScale = transform.parent.lossyScale;
        float sx = Mathf.Abs(parentScale.x) < 0.000001f ? 1f : parentScale.x;
        float sy = Mathf.Abs(parentScale.y) < 0.000001f ? 1f : parentScale.y;
        float sz = Mathf.Abs(parentScale.z) < 0.000001f ? 1f : parentScale.z;

        transform.localScale = new Vector3(worldScale.x / sx, worldScale.y / sy, worldScale.z / sz);



    }

    private void RestaurarColisionesConOwner()
    {
        if (ownerColliders == null) return;

        foreach (var c in ownerColliders)
        {
            if (c == null) continue;
            Physics.IgnoreCollision(physicalCollider, c, false);
            //Physics.IgnoreCollision(stealTriggerCollider, c, false);
        }

        ownerColliders = null;
    }
    public void Soltar()
    {
        gameObject.layer = LayerMask.NameToLayer("Default");

        RestaurarColisionesConOwner();

        EnPosesion = false;
        Owner = null;

        transform.SetParent(null, true);

        physicalCollider.enabled = true;

        rb.isKinematic = false;
        rb.useGravity = true;

     
        blockPickupUntil = Time.time + 0.25f;
        rb.WakeUp();
    }
}