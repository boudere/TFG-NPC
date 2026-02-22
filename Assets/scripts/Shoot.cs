using UnityEngine;

public class Shoot : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.O))
        {
            Bola.instance.Soltar();


            Rigidbody rb = Bola.instance.GetComponent<Rigidbody>();

            //rb.isKinematic = false;
            //rb.useGravity = true;


            Vector3 direction = Bola.instance.transform.forward;

            float passSpeed = 200f;
            rb.linearVelocity = direction * passSpeed;
            rb.angularVelocity = Vector3.zero;

            rb.WakeUp();
        }
    }
}
