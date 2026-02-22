using System.Collections;
using UnityEngine;

public class Delantero : PlayerID, IResettable
{
    private Vector3 spawnPos;
    private Quaternion spawnRot;
    private bool resetPos = false;
    private Rigidbody rb;
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
}
