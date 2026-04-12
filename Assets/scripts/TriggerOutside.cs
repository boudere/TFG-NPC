using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;


public class TriggerOutside : MonoBehaviour
{
    public string dimension;
    public FieldLimits field;
    private Bola bola;

   
    void Start()
    {
        field = FieldLimits.instance;
        bola = Bola.instance;
    }
    private void OnTriggerEnter(Collider other)
    {
       
        if (other.CompareTag("Ball") || Bola.instance.transform.IsChildOf(other.transform))
        {
            StartCoroutine(ResetBallAfterDelay(other.gameObject));
        }
    }

    private IEnumerator ResetBallAfterDelay(GameObject ball)
    {
        GameObject[] allPlayers = GetAllFieldPlayers();

        foreach (GameObject go in allPlayers)
        {
                detectTag(go);
        }

            

        yield return new WaitForSeconds(2f);

        float media;

        media = (field.maxZ + field.minZ)/2;

     if (dimension == "Z-")
        {
            ball.transform.position = new Vector3(ball.transform.position.x, ball.transform.position.y, field.minZ + 5);
            Physics.SyncTransforms();
        } else if (dimension == "Z+")
        {
            ball.transform.position = new Vector3(ball.transform.position.x, ball.transform.position.y, field.maxZ - 5);
            Physics.SyncTransforms();
        } else if (dimension == "X-")
        {
            if (ball.transform.position.z >= media)
            {
                ball.transform.position = new Vector3(field.minX + 5, ball.transform.position.y, field.maxZ - 5);
                Physics.SyncTransforms();
            } else
            {
                ball.transform.position = new Vector3(field.minX + 5, ball.transform.position.y, field.minZ + 5);
                Physics.SyncTransforms();
            }
        } else if (dimension == "X+")
        {
            if (ball.transform.position.z >= media)
            {
                ball.transform.position = new Vector3(field.maxX - 5, ball.transform.position.y, field.maxZ);
                Physics.SyncTransforms();
            }
            else
            {
                ball.transform.position = new Vector3(field.maxX - 5, ball.transform.position.y, field.minZ);
                Physics.SyncTransforms();
            }
        }

            Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.Sleep();
            rb.WakeUp();
        }
       
       
    }

    public void detectTag(GameObject go)
    {
        if (go.CompareTag("CentroCampista"))
        {
            CentroCampista p = go.GetComponent<CentroCampista>();
            StartCoroutine(p.PararJugador(p));
        }
        else if (go.CompareTag("Defensa"))
        {
            Defensa p = go.GetComponent<Defensa>();
            StartCoroutine(p.PararJugador(p));
        }
        else if (go.CompareTag("Delantero"))
        {
            Delantero p = go.GetComponent<Delantero>();
            StartCoroutine(p.PararJugador(p));
        }
        else if (!go.CompareTag("Portero"))
        {
            CharacterGV p = go.GetComponent<CharacterGV>();
            StartCoroutine(p.PararJugador(p));
        }


    }

    private GameObject[] GetAllFieldPlayers()
    {
        List<GameObject> allPlayers = new List<GameObject>();

        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("Defensa"));
        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("CentroCampista"));
        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("Delantero"));

        return allPlayers.ToArray();
    }
}
