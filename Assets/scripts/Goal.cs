using UnityEngine;

public class Goal : MonoBehaviour
{

    [SerializeField] private GameObject modal;
    public static Goal instance;

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
        }
    }

    public void openModal()
    {
        if (modal != null)
        {
            modal.SetActive(true);
        }
    }

    public void closeModal()
    {
        if (modal != null)
        {
            modal.SetActive(false);
        }
    }
}
