using UnityEngine;
using System.Collections;

public class TriggerGoal : MonoBehaviour
{
    private int goalCounter = 0;
    public Goal goal;
    private Porteria porteria;
    private int team;

    private bool goalLocked = false;   // <-- candado

    void Awake()
    {
        goal = Goal.instance;
    }

    void Start()
    {
        porteria = GetComponentInParent<Porteria>();
        team = porteria.team;
    }

    private void OnTriggerEnter(Collider other)
    {
       
        if (goalLocked) return;
        var rb = other.attachedRigidbody;
        if (rb == null) return;

        GameObject ballRoot = rb.gameObject;

        if (!ballRoot.CompareTag("Ball")) return;

        goalLocked = true; 

        goalCounter++;
        Debug.Log($"GOAL! Counter {goalCounter} | collider={other.name} | ballRoot={ballRoot.name}");

        StartCoroutine(ResetBallAfterDelay(ballRoot));
        StartCoroutine(MostrarPanel());
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

        goalLocked = false;
    }

    private IEnumerator MostrarPanel()
    {
        goal.openModal();
        yield return new WaitForSeconds(1f);
        goal.closeModal();
    }
}