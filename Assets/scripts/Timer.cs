using UnityEngine;
using UnityEngine.SceneManagement;

public class Timer : MonoBehaviour
{
    public int matchSeconds = 300;
    private float timer = 0f;
    private bool matchEnded = false;

    void Start()
    {
        matchEnded = false;
        ShowTime.instance.SetTime(matchSeconds);
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

        SceneManager.LoadScene(5);
    }

    public bool IsMatchEnded()
    {
        return matchEnded;
    }   
}