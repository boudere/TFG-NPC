//using UnityEngine;

//public class Bola : MonoBehaviour
//{
//    public static Bola instance;

//    [Header("Refs")]
//    [SerializeField] private Rigidbody rb;
//    [SerializeField] private Collider physicalCollider;      // NO trigger
//    [SerializeField] private Collider stealTriggerCollider;  // SI trigger

//    [Header("Posesión")]
//    [SerializeField] private Vector3 localHoldOffset = new Vector3(0f, 0f, 1f);

//    public bool EnPosesion { get; private set; }
//    public PlayerID Owner { get; private set; }

//    private void Awake()
//    {
//        instance = this;
//        if (rb == null) rb = GetComponent<Rigidbody>();
//    }

//    public void AsignarPosesion(PlayerID newOwner)
//    {
//        Owner = newOwner;
//        EnPosesion = true;

//        // apagar física “real”
//        rb.linearVelocity = Vector3.zero;
//        rb.angularVelocity = Vector3.zero;
//        rb.isKinematic = true;
//        rb.useGravity = false;

//        // IMPORTANTÍSIMO:
//        // - desactiva collider físico para que NO empuje ni frene al jugador
//        physicalCollider.enabled = false;

//        // - deja el trigger activo para que otros puedan “robar”
//        stealTriggerCollider.enabled = true;

//        // pega al jugador (mejor a un punto "hold", si lo tienes)
//        transform.SetParent(newOwner.transform, worldPositionStays: false);
//       // transform.SetParent(newOwner.holdPoint, false);
//        //transform.localPosition = Vector3.zero;              // o tu offset
//        //transform.localRotation = Quaternion.identity;
//        //transform.localScale = Vector3.one;
//    }

//    public void Soltar()
//    {
//        EnPosesion = false;
//        Owner = null;

//        transform.SetParent(null, true);

//        physicalCollider.enabled = true;

//        rb.isKinematic = false;
//        rb.useGravity = true;
//    }
//}


using UnityEngine;

public class Bola : MonoBehaviour
{
    public static Bola instance;

    [Header("Refs")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Collider physicalCollider;      // NO trigger
    [SerializeField] private Collider stealTriggerCollider;  // SÍ trigger

    [Header("Posesión")]
    [SerializeField] private Vector3 localHoldOffset = new Vector3(0f, 0f, 1f);

    public bool EnPosesion { get; private set; }
    public PlayerID Owner { get; private set; }
    private Collider[] ownerColliders;

    private void Awake()
    {
        instance = this;

        if (rb == null)
            rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (!EnPosesion) return;


        Debug.Log($"RB kinematic={rb.isKinematic}, vel={rb.linearVelocity}, physEnabled={physicalCollider.enabled}, triggerEnabled={stealTriggerCollider.enabled}");
    }

    public void AsignarPosesion(PlayerID newOwner)


    {
        Debug.Log($"physicalCollider: trigger={physicalCollider.isTrigger}, enabled={physicalCollider.enabled}");
        Debug.Log($"stealTriggerCollider: trigger={stealTriggerCollider.isTrigger}, enabled={stealTriggerCollider.enabled}");

        // Si ya la tiene este jugador, no rehagas todo
        if (EnPosesion && Owner == newOwner) return;

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
        stealTriggerCollider.enabled = true;
        stealTriggerCollider.isTrigger = true;


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
            Physics.IgnoreCollision(stealTriggerCollider, c, false);
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
    }
}