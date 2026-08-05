using UnityEngine;
using UnityEngine.SceneManagement;

public class Timer : MonoBehaviour
{
   
    private float timer = 0f;
    private bool matchEnded = false;
    private int matchSeconds;

    void Start()
    {
        matchEnded = false;
        matchSeconds = Data.instance.matchSeconds;
        ShowTime.instance.SetTime(Data.instance.matchSeconds);
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= 1f)
        {
            timer -= 1f;
            matchSeconds--;

            if (ShowTime.instance != null)
                ShowTime.instance.SetTime(matchSeconds);

            if (matchSeconds <= 0)
            {
                EndMatch();
            }
        }
    }

    void EndMatch()
    {
        Debug.Log("Fin del partido");
        Time.timeScale = 0f;
        matchEnded = true;

        if (Data.instance.esJuego)
        {
            SceneManager.LoadScene(6);
        } else
        {
            SceneManager.LoadScene(8);
        }
           
    }

    public bool IsMatchEnded()
    {
        return matchEnded;
    }   
}