using System;
using System.Collections;
using UnityEngine;
using UnityEngine.LowLevel;
using Random = UnityEngine.Random;

public class CentroCampista : PlayerID, IResettable
{


    private Vector3 spawnPos;
    private Quaternion spawnRot;
    private bool resetPos = false;
    private Rigidbody rb;
    public bool pase = false;
    private FieldLimits field;

    [SerializeField] private float turnSpeedDeg = 540f;     // velocidad de giro
    [SerializeField] private float directionLerp = 12f;     // suaviza cambios bruscos
    private Vector3 smoothDir = Vector3.forward;           // dirección suavizada
    private float speed = 150;
    private float npcSpeed = 75;
    private float changeTargetDistance = 50f;
    private Vector3 npcTarget;

    private CharacterManager characterManager;

    //Probabilidad de efectuar pase 
    private float npcPass = 0.002f; //PROVISIONAL

    public bool defaultMove = true;

    private bool frozen = false;
    private GameObject playerStop = null;

    private float moveInArea = 0.5f;

    private AreaPeligro1 ap1;
    private AreaPeligro2 ap2;



    private void Awake()
    {

        spawnPos = transform.position;
        spawnRot = transform.rotation;
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        characterManager = CharacterManager.instance;
        field = FieldLimits.instance;
        ap1 = AreaPeligro1.instance;
        ap2 = AreaPeligro2.instance;
        PickNewTarget(); 
    }


    //void Update()
    //{
    //    if (frozen && playerStop)
    //    {
    //        StartCoroutine(StopAndRetargetRoutine(playerStop));
    //    }

    //    if (resetPos)
    //    {
    //        return;
    //    }
    //    move();

    //}

    void FixedUpdate()
    {
        rotacion();
    }

    void rotacion()
    {
        if (this.id == characterManager.index) { return; }
        Vector3 dir = npcTarget - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude <= 0.001f) return;

        dir.Normalize();

        smoothDir = Vector3.Slerp(smoothDir, dir, directionLerp * Time.fixedDeltaTime);

        Quaternion targetRot = Quaternion.LookRotation(smoothDir, Vector3.up);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, turnSpeedDeg * Time.fixedDeltaTime));
    }


    private void move()
    {

        if (this.id == characterManager.index) { return; }


        Vector3 direction = (npcTarget - transform.position).normalized;

        rb.linearVelocity = new Vector3(
            direction.x * npcSpeed,
            rb.linearVelocity.y,
            direction.z * npcSpeed
        );

        if (Vector3.Distance(transform.position, npcTarget) < changeTargetDistance)
            PickNewTarget();
    }

    void PickNewTarget()
    {
  

        float x, z;
        if (Random.value < moveInArea)
        {
            x = Random.Range(field.minX, field.maxX);
            z = Random.Range(field.minZ, field.maxZ);
        }
        else
        {
                x = Random.Range(ap2.minX, ap1.maxX);
                z = Random.Range(field.minZ, field.maxZ);
        }

            npcTarget = new Vector3(x, transform.position.y, z);
    }

    private void OnEnable()
    {
        Pase.OnCharacterGVSelected += OnSelected;
    }

    private void OnDisable()
    {
        Pase.OnCharacterGVSelected -= OnSelected;
    }

    void OnSelected(GameObject player)
    {
        frozen = true;
        playerStop = player;
    }

    private IEnumerator StopAndRetargetRoutine(GameObject playerStop)
    {
        Rigidbody rbPlayer = playerStop.GetComponent<Rigidbody>();
        rbPlayer.linearVelocity = Vector3.zero;
        rbPlayer.angularVelocity = Vector3.zero;
        yield return new WaitForSeconds(2f);
        frozen = false;
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
