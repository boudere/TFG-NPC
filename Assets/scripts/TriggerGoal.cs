using UnityEngine;
using System.Collections;


public class TriggerGoal : MonoBehaviour
{

    private int goalCounter = 0;
    public Goal goal;

    void Awake()
    {
        goal = Goal.instance;
    }
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Ball"))
        {
            goalCounter++;
            StartCoroutine(ResetBallAfterDelay(other.gameObject));
            StartCoroutine(MostrarPanel());
        }
    }

    private IEnumerator ResetBallAfterDelay(GameObject ball)
    {
        yield return new WaitForSeconds(1f); 

        ball.transform.position = new Vector3(59, 9.391f, 80.6f);

        Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private IEnumerator MostrarPanel()
    {
        goal.openModal();
        yield return new WaitForSeconds(1f);
        goal.closeModal();
    }
}
