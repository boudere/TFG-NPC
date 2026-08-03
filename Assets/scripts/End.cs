using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class End : MonoBehaviour
{
    [SerializeField] private Image equipoGanador;
    [SerializeField] private Image endLabel;
    [SerializeField] private Image copa;

    public Sprite ganaAzul;
    public Sprite ganaRojo;
    public Sprite ningunoGana;

    public Sprite campeones;
    public Sprite empate;

    void Start()
    {
        copa.enabled = true;

        if (Data.instance.redTeam > Data.instance.blueTeam)
        {
            endLabel.sprite = campeones;
            equipoGanador.sprite = ganaRojo;
        }
        else if (Data.instance.redTeam < Data.instance.blueTeam)
        {
            endLabel.sprite = campeones;
            equipoGanador.sprite = ganaAzul;
        }
        else
        {
            endLabel.sprite = empate;
            equipoGanador.sprite = ningunoGana;
            copa.enabled = false;
        }

        StartCoroutine(CambiarEscena());
    }

    IEnumerator CambiarEscena()
    {
        // Espera 3 segundos reales aunque Time.timeScale sea 0
        yield return new WaitForSecondsRealtime(3f);

        // Por si la siguiente escena necesita que el tiempo vuelva a funcionar
        Time.timeScale = 1f;

        SceneManager.LoadScene(7);
    }
}