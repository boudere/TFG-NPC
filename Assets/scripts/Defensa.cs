using System.Collections;
using UnityEngine;

public class Defensa : PlayerID, IResettable
{
    private Vector3 spawnPos;
    private Quaternion spawnRot;
    private bool resetPos = false;
    private Rigidbody rb;
    public bool pase = false;

    //Probabilidades (luego pueden fallarse o no) 

    //Tirarla libre 10%
    //Tirarla a un jugador cualquiera 25%
    //Tirarla a un centro del campo 65% 

    /*
     En caso de que se pueda dar asistencia ...
     */


    private void Awake()
    {

        spawnPos = transform.position;
        spawnRot = transform.rotation;
        rb = GetComponent<Rigidbody>();
    }
 
    void Start()
    {
        
    }

    
    void Update()
    {
        if (resetPos)
        {
            return;
        }

        if (Bola.instance.transform.IsChildOf(transform))
        {
            // Si tienen la bola, probabilidad de pasarla a un centro campista, probabilidad de pasarlo a otro player o de disparar libremente 
            float aux = Random.value;

           // if (aux >)

            //También debería pararlo no ?

        }

        // Perseguir a delantero si está en área de defensa 


        // Contar jugadores por campo y si hay más en el otro ir  hacia allá
        }


    public void ResetToSpawn()
    {

        transform.position = spawnPos;
        transform.rotation = spawnRot;
        resetPos = true;

        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        rb.angularVelocity = Vector3.zero;

        StartCoroutine(ResetRoutine());


    }

    private IEnumerator ResetRoutine()
    {
        yield return new WaitForSeconds(3f);
        resetPos = false;
    }

    public void setPase(bool pase)
    {
        this.pase = pase;
    }
}
