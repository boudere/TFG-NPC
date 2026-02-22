using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class Portero : PlayerID
{

    public float minX, maxX, minZ, maxZ, y;
    public float lineaGolMax, lineaGolMin, hx;


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
    public bool defendiendo = false;
    [SerializeField] private float velocidadDefensa = 50f;
    private int direccionDefensa = 1;
    private bool saqueEnProceso = false;

    private Vector3 spawnPos;
    private Quaternion spawnRot;
    private bool resetPos = false;

    //Probabilidades saque
    float probabilidadSaque = 0.05f;
    float exito = 0.3f;
    private void Awake()
    {

        spawnPos = transform.position;
        spawnRot = transform.rotation;
        rb = GetComponent<Rigidbody>();
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
        if (resetPos)
        {
            return;
        }


        if (Bola.instance.transform.IsChildOf(transform))
        {
            saquePorteria();
        }



        if (frozen && playerStop)
        {
            StartCoroutine(StopAndRetargetRoutine(playerStop));
        }

        if (defendiendo)
        {

            DefensaBehaviour();
            return;
        }


        othersBehaviour();

    }

    void saquePorteria()
    {
        if (saqueEnProceso) return;


        float aux = Random.value;
        if (aux < probabilidadSaque)
        {
            saqueEnProceso = true;
            StartCoroutine(EsperarYSacar(exito));
        }
    }

    IEnumerator EsperarYSacar(float exito)
    {
        yield return new WaitForSeconds(3f);

        Bola.instance.Soltar();

        Rigidbody rb = Bola.instance.GetComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;

        if (Random.value < exito)
        {

            Pase.instance.searchPlayersToPass("portero", transform.position, this.id);
        }
        else
        {

            disparoAleatorio();
        }

        saqueEnProceso = false;
        rb.WakeUp();
    }

    void disparoAleatorio()
    {
        Vector3 direction = Bola.instance.transform.forward;

        float passSpeed = 200f;
        rb.linearVelocity = direction * passSpeed;
        rb.angularVelocity = Vector3.zero;
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
    }

    void PickNewTarget()
    {
        float x = Random.Range(minX, maxX);
        float z = Random.Range(minZ, maxZ);

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

    void DefensaBehaviour()
    {
        Vector3 pos = transform.position;


        pos.x = hx;

        pos.z += direccionDefensa * velocidadDefensa * Time.deltaTime;

        if (pos.z >= lineaGolMax)
        {
            pos.z = lineaGolMax;
            direccionDefensa = -1;
        }
        else if (pos.z <= lineaGolMin)
        {
            pos.z = lineaGolMin;
            direccionDefensa = 1;
        }

        transform.position = pos;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }


    public void PararDefensa()
    {
        defendiendo = false;
        PickNewTarget();
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
