using System;
using UnityEngine;
using UnityEngine.UI;

public class ShowScore : MonoBehaviour
{
    [SerializeField] private GameObject modal;
    public static ShowScore instance;

    [Header("Marcador izquierdo")]
    [SerializeField] private Image scoreTeam0Image;   // decenas
    [SerializeField] private Image scoreTeam00Image;  // unidades

    [Header("Marcador derecho")]
    [SerializeField] private Image scoreTeam1Image;   // decenas
    [SerializeField] private Image scoreTeam11Image;  // unidades

    [SerializeField] private Sprite[] digits = new Sprite[10];

    private int leftScore = 0;
    private int rightScore = 0;

    

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

    private void Start()
    {
        if (modal != null)
            modal.SetActive(true);

        UpdateLeftScore();
        UpdateRightScore();
    }

    public void openModalMarcador()
    {
        if (modal != null)
            modal.SetActive(false);
    }

    public void closeModalMarcador()
    {
        if (modal != null)
            modal.SetActive(true);
    }

    // team0 se muestra cruzado en el marcador derecho
    public void setScoret0(int value)
    {
        rightScore = Mathf.Clamp(value, 0, 99);
        UpdateRightScore();
        Data.instance.redTeam = rightScore;
        Debug.Log($"{rightScore} red");
    }

    // team1 se muestra cruzado en el marcador izquierdo
    public void setScoret1(int value)
    {
        leftScore = Mathf.Clamp(value, 0, 99);
        UpdateLeftScore();
        Data.instance.blueTeam = leftScore;
        Debug.Log($"{leftScore} blue");
    }

    private void UpdateLeftScore()
    {
        int decenas = leftScore / 10;
        int unidades = leftScore % 10;



        if (scoreTeam0Image != null)
        {
            scoreTeam0Image.enabled = leftScore >= 10;
            if (leftScore >= 10)
                scoreTeam0Image.sprite = digits[decenas];
        }

        if (scoreTeam00Image != null)
            scoreTeam00Image.sprite = digits[unidades];


    }

    private void UpdateRightScore()
    {
        int decenas = rightScore / 10;
        int unidades = rightScore % 10;

        if (scoreTeam1Image != null)
        {
            scoreTeam1Image.enabled = rightScore >= 10;
            if (rightScore >= 10)
                scoreTeam1Image.sprite = digits[decenas];
        }

        if (scoreTeam11Image != null)
            scoreTeam11Image.sprite = digits[unidades];
    }


}