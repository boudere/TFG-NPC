using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class Goal : MonoBehaviour
{
    [SerializeField] private GameObject modal;
    [SerializeField] private GameObject exitModal;
    [SerializeField] private GameObject pausarBoton;
    [SerializeField] private GameObject reanudarBoton;
    [SerializeField] private GameObject salirBoton;

    [Header("Árbitro")]
    [SerializeField] private GameObject arbitroPanel;
    [SerializeField] private GameObject arbitroButon;
    [SerializeField] private TMP_Text arbitroLogText;
    [SerializeField] private ScrollRect arbitroScrollRect;

    [Header("Configuración Logs")]
    [SerializeField] private int maxLogs = 1000;

    public static Goal instance;
    private float posicionScrollArbitro = 1f;
    private readonly List<string> logsArbitro = new List<string>();

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        posicionScrollArbitro = 1f;
        if (modal != null)
            modal.SetActive(false);

        if (exitModal != null)
            exitModal.SetActive(false);

        if (pausarBoton != null)
            pausarBoton.SetActive(true);

        if (reanudarBoton != null)
            reanudarBoton.SetActive(false);

        if (arbitroPanel != null)
            arbitroPanel.SetActive(false);

        if (arbitroButon != null)
            arbitroButon.SetActive(true);

        if (arbitroLogText != null)
            arbitroLogText.text = "";
    }

    public void openModalGoal()
    {
        if (modal != null)
        {
            modal.SetActive(true);
        }
    }

    public void closeModalGoal()
    {
        if (modal != null)
        {
            modal.SetActive(false);
        }
    }

    public void salirSi()
    {
        Timer.instance.EndMatch();
    }

    public void salirNo()
    {
        exitModal.SetActive(false);
        pausarBoton.SetActive(true);
        salirBoton.SetActive(true);

        Timer.instance.Reanudar();
    }

    public void pausar()
    {
        Timer.instance.Pausar();

        pausarBoton.SetActive(false);
        salirBoton.SetActive(false);
        reanudarBoton.SetActive(true);
    }

    public void reanudar()
    {
        Timer.instance.Reanudar();

        pausarBoton.SetActive(true);
        salirBoton.SetActive(true);
        reanudarBoton.SetActive(false);
    }

    public void terminarPartida()
    {
        exitModal.SetActive(true);
        salirBoton.SetActive(false);
        pausarBoton.SetActive(false);

        Timer.instance.Pausar();
    }

    // =========================
    // ÁRBITRO
    // =========================

    public void arbitro()
    {
        arbitroPanel.SetActive(true);
        arbitroButon.SetActive(false);

        StartCoroutine(RestaurarPosicionScroll());
    }

    public void closeArbitro()
    {
        if (arbitroScrollRect != null)
        {
            posicionScrollArbitro =
                arbitroScrollRect.verticalNormalizedPosition;
        }

        arbitroPanel.SetActive(false);
        arbitroButon.SetActive(true);
    }

    public void LogArbitro(string mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje))
            return;

        logsArbitro.Add(mensaje);

        if (logsArbitro.Count > maxLogs)
        {
            logsArbitro.RemoveAt(0);
        }

        ActualizarTextoArbitro();

        if (arbitroPanel.activeInHierarchy)
        {
            StartCoroutine(RestaurarPosicionScroll());
        }
    }

    private void ActualizarTextoArbitro()
    {
        if (arbitroLogText == null)
            return;

        arbitroLogText.text = string.Join("\n", logsArbitro);
    }

    private IEnumerator RestaurarPosicionScroll()
    {
        yield return null;

        Canvas.ForceUpdateCanvases();

        if (arbitroLogText != null)
        {
            arbitroLogText.ForceMeshUpdate();
        }

        if (arbitroScrollRect != null &&
            arbitroScrollRect.content != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                arbitroScrollRect.content
            );

            Canvas.ForceUpdateCanvases();

            arbitroScrollRect.verticalNormalizedPosition =
                posicionScrollArbitro;
        }
    }

    public void LimpiarLogsArbitro()
    {
        logsArbitro.Clear();

        if (arbitroLogText != null)
        {
            arbitroLogText.text = "";
        }

        // Al limpiar volvemos al principio
        posicionScrollArbitro = 1f;

        if (arbitroScrollRect != null)
        {
            arbitroScrollRect.verticalNormalizedPosition = 1f;
        }
    }

}