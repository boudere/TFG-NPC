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
}
