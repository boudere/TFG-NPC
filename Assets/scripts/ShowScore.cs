using UnityEngine;
using UnityEngine.UI;

public class ShowScore : MonoBehaviour
{

    [SerializeField] private GameObject modal;
    public static ShowScore instance;
    [SerializeField] private Image scoreTeam0Image;
    [SerializeField] private Image scoreTeam1Image; 
    [SerializeField] private Sprite[] digits = new Sprite[10];
    int team0 = 0;
    int team1 = 0;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

    }

    void Start()
    {
        if (modal != null)
        {
            modal.SetActive(false);
            scoreTeam0Image.sprite = digits[team0];
            scoreTeam1Image.sprite = digits[team1];
        }
    }

    public void openModalMarcador()
    {
        if (modal != null)
        {
            modal.SetActive(true);
        }
    }

    public void closeModalMarcador()
    {
        if (modal != null)
        {
            modal.SetActive(false);
        }
    }

 

    public void setScoret0(int team0)
    {
        int t0 = Mathf.Clamp(team0, 0, 9);
        if (scoreTeam0Image != null) scoreTeam0Image.sprite = digits[team0];
    }

    public void setScoret1(int team1)
    {
        int t1 = Mathf.Clamp(team1, 0, 9);
        if (scoreTeam1Image != null) scoreTeam0Image.sprite = digits[team1];
    }

}
