using UnityEngine;

public class Shoot : MonoBehaviour
{

    public static Shoot instance;

    private void Awake()
    {
        instance = this;

    }
    void Update()
    {
        if (InputLock.GetKeyDown(KeyCode.O))
        {
            if (Bola.instance != null && Bola.instance.EnPosesion && Bola.instance.Owner != null && Bola.instance.Owner.id == CharacterManager.instance.index)
            {
                disparoLibre();
            }
        }
    }

    public void disparoLibre()
    {
        Bola.instance.Soltar();


        Rigidbody rb = Bola.instance.GetComponent<Rigidbody>();



        Vector3 direction = Bola.instance.transform.forward;

        float passSpeed = 200f;
        rb.linearVelocity = direction * passSpeed;
        rb.angularVelocity = Vector3.zero;

    }
}

