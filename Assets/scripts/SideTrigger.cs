using System.Collections.Generic;
using UnityEngine;

public class SideTrigger : MonoBehaviour
{
    public int sideIndex;
    private List<int> playersInAreaId = new List<int>();
    private bool ballInside = false;
    public int team;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Ball"))
        {
            ballInside = true;
            return;
        }

        PlayerID jugador = other.GetComponent<PlayerID>();
        if (jugador != null && !playersInAreaId.Contains(jugador.id))
        {
            playersInAreaId.Add(jugador.id);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Ball"))
        {
            ballInside = false;
            return;
        }

        PlayerID jugador = other.GetComponent<PlayerID>();
        if (jugador != null)
        {
            playersInAreaId.Remove(jugador.id);
        }
    }

    public bool IsBallInside()
    {
        return ballInside;
    }
}