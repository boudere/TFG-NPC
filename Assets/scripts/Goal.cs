using UnityEngine;

public class Goal : MonoBehaviour
{

    [SerializeField] private GameObject modal;
    [SerializeField] private GameObject exitModal;
    [SerializeField] private GameObject pausarBoton;
    [SerializeField] private GameObject reanudarBoton;
    [SerializeField] private GameObject salirBoton;
    public static Goal instance;

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

    void Start()
    {
        if (modal != null)
        {
            modal.SetActive(false);
            exitModal.SetActive(false);
            pausarBoton.SetActive(true);
            reanudarBoton.SetActive(false);
        }

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
}
