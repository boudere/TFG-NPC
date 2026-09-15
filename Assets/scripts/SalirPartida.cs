using UnityEngine;

public class SalirPartida : MonoBehaviour
{
    [SerializeField] private GameObject salirBoton;
    [SerializeField] private GameObject exitModal;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void salirSi()
    {
        Application.Quit();
    }

    public void salirNo()
    {
        exitModal.SetActive(false);
        salirBoton.SetActive(true);

       
    }

    public void terminarPartida()
    {
        exitModal.SetActive(true);
        salirBoton.SetActive(false);

    }
}
