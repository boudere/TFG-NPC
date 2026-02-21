using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class CharacterGV : MonoBehaviour
{

    [SerializeField] private float turnSpeedDeg = 540f;     // velocidad de giro
    [SerializeField] private float directionLerp = 12f;     // suaviza cambios bruscos
    private Vector3 smoothDir = Vector3.forward;           // dirección suavizada
    private float speed = 150;
    private float npcSpeed = 75;

    private float changeTargetDistance = 50f;

    private Vector3 npcTarget;


    private Rigidbody rb;
    public int index;
    private CharacterManager characterManager;
    private FieldLimits field;
    public static CharacterGV instance;

    private bool frozen = false;
    private GameObject playerStop = null;

    private Vector3 spawnPos;
    private Quaternion spawnRot;
    private bool resetPos = false;

    private void Awake()
    {


        rb = GetComponent<Rigidbody>();
        instance = this;

        spawnPos = transform.position;
        Debug.Log(spawnPos);
        spawnRot = transform.rotation;
    }

    private void Start()
    {
        characterManager = CharacterManager.instance;
        field = FieldLimits.instance;

        if (index != characterManager.index)
            PickNewTarget();


    }

    private void Update()
    {
        if (frozen && playerStop)
        {
            StartCoroutine(StopAndRetargetRoutine(playerStop));
        }
        if (resetPos)
        {
            return;
        }
        movePlayer();
        othersBehaviour();
    }



    void movePlayer()
    {
        if (index != characterManager.index)
            return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 inputDir = new Vector3(h, 0f, v);

        // Movimiento (en la dirección que toca)
        Vector3 moveDir = inputDir.normalized;
        Vector3 movement = moveDir * speed;
        rb.linearVelocity = new Vector3(movement.x, rb.linearVelocity.y, movement.z);


        if (inputDir.sqrMagnitude > 0.001f)
        {

            smoothDir = Vector3.Slerp(smoothDir, moveDir, directionLerp * Time.deltaTime);

            Quaternion targetRot = Quaternion.LookRotation(smoothDir, Vector3.up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, turnSpeedDeg * Time.deltaTime));
        }
    }

    void othersBehaviour()
    {
        //Hacer un swich con las distinas posibilidades (ir a un destino y llegar al objetivo), estar quieto
        if (index == characterManager.index)
            return;

        Vector3 direction = (npcTarget - transform.position).normalized;

        rb.linearVelocity = new Vector3(
            direction.x * npcSpeed,
            rb.linearVelocity.y,
            direction.z * npcSpeed
        );

        if (Vector3.Distance(transform.position, npcTarget) < changeTargetDistance)
            PickNewTarget();

        //StartCoroutine(ControlarTiempo());
    }

    //private IEnumerator ControlarTiempo()
    //{
    //    float distanciaTotal = 0f;
    //    Vector3 ultimaPosicion = transform.position;

    //    int segundos = 0;

    //    while (segundos < 2)
    //    {
    //        yield return new WaitForSeconds(1f);

    //        Vector3 posicionActual = transform.position;
    //        float distancia = Vector3.Distance(ultimaPosicion, posicionActual);

    //        distanciaTotal += distancia;
    //        ultimaPosicion = posicionActual;

    //        segundos++;
    //    }

    //    if (distanciaTotal < 15f)
    //    {
    //        PickNewTarget();
    //    }

    //}



    void PickNewTarget()
    {
        float x = Random.Range(field.minX, field.maxX);
        float z = Random.Range(field.minZ, field.maxZ);

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

}


