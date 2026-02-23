using UnityEngine;
using System.Collections;
using System;
using System.Linq;

public class TriggerGoal : MonoBehaviour
{
    private int goalCounter = 0;
    public Goal goal;
    public ShowScore showScore;
    private Porteria porteria;
    private int team;
    

    private bool goalLocked = false;   

    void Awake()
    {
        goal = Goal.instance;
        porteria = GetComponentInParent<Porteria>();
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
        porteria.goalCounterTeam++;

        StartCoroutine(ResetBallAfterDelay(ballRoot));
        searchPlayersReset();
        StartCoroutine(GoalThenScoreSequence());



    }

    private IEnumerator GoalThenScoreSequence()
    {
        yield return StartCoroutine(MostrarPanelGoal());
        yield return StartCoroutine(MostrarPanelMarcador());

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

    private IEnumerator MostrarPanelGoal()
    {
        goal.openModalGoal();
        yield return new WaitForSeconds(1f);
        goal.closeModalGoal();
    }

    private IEnumerator MostrarPanelMarcador()
    {

        GameObject[] p = GameObject.FindGameObjectsWithTag("Porteria");
        int id;

        for (int i = 0; i < p.Length; i++)
        {
            id = p[i].GetComponent<Porteria>().team; 
            if (team % 2 == 0)
            {
                showScore.setScoret0(porteria.goalCounterTeam);
            } else if (team % 2 == 1)
            {
                showScore.setScoret1(porteria.goalCounterTeam);
            }
        }

           
        showScore.openModalMarcador();
        yield return new WaitForSeconds(1f);
        showScore.closeModalMarcador();
    }

   private void searchPlayersReset()
    {
        resetByTag<Portero>("Portero");
        resetByTag<Defensa>("Defensa");
        resetByTag<CentroCampista>("CentroCampista");
        resetByTag<Delantero>("Delantero");

    }

    private void resetByTag<T>(string tag) where T : PlayerID, IResettable
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag(tag);
        foreach (GameObject p in players)
        {
            T character =p.GetComponent<T>();

            if (character != null)
            {
                character.ResetToSpawn();
            }
        }
          
    }
}
