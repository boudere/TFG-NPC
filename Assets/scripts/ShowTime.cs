using UnityEngine;
using UnityEngine.UI;

public class ShowTime : MonoBehaviour
{
    [SerializeField] private GameObject modal;

    public static ShowTime instance;

    [SerializeField] private Image time00Image;
    [SerializeField] private Image time01Image;
    [SerializeField] private Image time10Image;
    [SerializeField] private Image time11Image;

    [SerializeField] private Sprite[] digits = new Sprite[10];

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
        SetTime(300); // empieza en 05:00
    }

    public void SetTime(int totalSeconds)
    {
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        int mTens = minutes / 10;
        int mUnits = minutes % 10;
        int sTens = seconds / 10;
        int sUnits = seconds % 10;

        if (time00Image != null) time00Image.sprite = digits[mTens];
        if (time01Image != null) time01Image.sprite = digits[mUnits];
        if (time10Image != null) time10Image.sprite = digits[sTens];
        if (time11Image != null) time11Image.sprite = digits[sUnits];
    }
}