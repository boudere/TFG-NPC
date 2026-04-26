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
        if (Input.GetKeyDown(KeyCode.O))
        {
            disparoLibre();
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

       // rb.WakeUp();
    }
}

