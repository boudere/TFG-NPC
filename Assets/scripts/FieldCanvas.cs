using UnityEngine;
using UnityEngine.SceneManagement;

public class FieldCanvas : MonoBehaviour
{
    [SerializeField] private GameObject modal;
    void Start()
    {
        if (modal != null)
        {
            modal.SetActive(false);
        }
    }
    void Update()
    {
       
    }

  public  void openModal()
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

    public void goToSelectCharacter()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }

    public void changeCamara()
    {
        SceneManager.UnloadSceneAsync(SceneManager.GetActiveScene().buildIndex + 2);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 3, LoadSceneMode.Additive);
    }
}
