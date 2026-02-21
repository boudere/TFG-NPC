using UnityEngine;

public class Porteria : MonoBehaviour
{
    public int team;
    public int lineaGolMax, lineaGolMin, hx;
    public int goalCounterTeam = 0;
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
     protected void goalScored()
    {
        goalCounterTeam++;
    }

    public int getNumberGoals()
    {
        return goalCounterTeam;
    }
}
