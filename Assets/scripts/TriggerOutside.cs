using UnityEngine;
using System.Collections;
using System.Security.Cryptography;


public class TriggerOutside : MonoBehaviour
{
    public string dimension;
    public FieldLimits field;

    void Start()
    {
        field = FieldLimits.instance;
    }
    private void OnTriggerEnter(Collider other)
    {
       
        if (other.CompareTag("Ball"))
        {
            StartCoroutine(ResetBallAfterDelay(other.gameObject));
        }
    }

    private IEnumerator ResetBallAfterDelay(GameObject ball)
    {
        yield return new WaitForSeconds(3f);

        float media;

        media = (field.maxZ + field.minZ)/2;

     if (dimension == "Z-")
        {
            ball.transform.position = new Vector3(ball.transform.position.x, ball.transform.position.y, field.minZ + 5);
            Physics.SyncTransforms();
        } else if (dimension == "Z+")
        {
            ball.transform.position = new Vector3(ball.transform.position.x, ball.transform.position.y, field.maxZ - 5);
            Physics.SyncTransforms();
        } else if (dimension == "X-")
        {
            if (ball.transform.position.z >= media)
            {
                ball.transform.position = new Vector3(field.minX + 5, ball.transform.position.y, field.maxZ - 5);
                Physics.SyncTransforms();
            } else
            {
                ball.transform.position = new Vector3(field.minX + 5, ball.transform.position.y, field.minZ + 5);
                Physics.SyncTransforms();
            }
        } else if (dimension == "X+")
        {
            if (ball.transform.position.z >= media)
            {
                ball.transform.position = new Vector3(field.maxX - 5, ball.transform.position.y, field.maxZ);
                Physics.SyncTransforms();
            }
            else
            {
                ball.transform.position = new Vector3(field.maxX - 5, ball.transform.position.y, field.minZ);
                Physics.SyncTransforms();
            }
        }

            Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.Sleep();
            rb.WakeUp();
        }
       
       
    }
}
