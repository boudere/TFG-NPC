using System;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.LowLevel;
using Random = UnityEngine.Random;

public class Delantero : PlayerID, IResettable
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
    private float npcPass = 0.01f; //PROVISIONAL

    public bool defaultMove = true;

    private bool frozen = false;
    private GameObject playerStop = null;

    private float moveInArea = 0.5f;
    private AreaPeligro1 ap1;
    private AreaPeligro2 ap2;
    private AreaPeligro ap;

    private float tiroCase1 = 0.2f; // Tiro aleatorio
    private float tiroCase2 = 0.5f; //Pase a un npc
                                    // Ir hacia porteria y tirar si no se la roban 

    private bool disparoPorteria = false;
    

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
        ap = AreaPeligro.instance;
        PickNewTarget(); 
    }

    /*
     Si tienen la bola que se acerque a la portería y apunte, también que la pase a un medio campo, o que se pueda a equivocar 
     */
    void Update()
    {
        if (frozen && playerStop)
        {
            StartCoroutine(StopAndRetargetRoutine(playerStop));
        }

        if (resetPos)
        {
            return;
        }


        if (Bola.instance.transform.IsChildOf(transform))
        {
            opcionPase();
        }
        else
        {
            disparoPorteria = false;
        }

        if (chasingBall)
            runToBall();
        else
            move();

    }

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
        {
            PickNewTarget();
        }
       
    }

    private void opcionPase()
    {
        if (this.id == characterManager.index) { return; }

        if (disparoPorteria)
        {
            return;
        }

        if (Random.value < npcPass)
        {
            float aux = Random.value;
            if (aux < tiroCase1)
            {
                Shoot.instance.disparoLibre();
            }
            else if (tiroCase1 < aux && aux < tiroCase2)
            {
                Pase.instance.searchPlayersToPass("npc", transform.position, this.id);
            }
            else
            {
                tiroPorteria();
            }
        }
    }

    void PickNewTarget()
    {
        float x = 0, z = 0;
        if (Random.value < moveInArea)
        {

            GameObject[] areas = GameObject.FindGameObjectsWithTag("AreaPeligro");

            foreach (GameObject a in areas)
            {
                AreaPeligro area = a.GetComponent<AreaPeligro>();

                if (area != null && area.TEAM != this.id % 2)
                { 
                    x = Random.Range(area.minX, area.maxX);
                    z = Random.Range(area.minZ, area.maxZ);
                }
            }

            //if (this.id % 2 == 1)
            //{
            //    x = Random.Range(ap1.minX, ap1.maxX);
            //    z = Random.Range(ap1.minZ, ap1.maxZ);

            //}
            //else
            //{
            //    x = Random.Range(ap2.minX, ap2.maxX);
            //    z = Random.Range(ap2.minZ, ap2.maxZ);
            //}

        }
        else
        {
            x = Random.Range(field.minX, field.maxX);
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

    public void activarTiro()
    {
        disparoPorteria = true;
    }

    public void desactivarTiro()
    {
        disparoPorteria = false;
    }

    private void tiroPorteria()
    {
        GameObject[] porterias = GameObject.FindGameObjectsWithTag("Porteria");
        Vector3 dir = new Vector3(0,0);

        foreach (GameObject p in porterias)
        {
            Porteria porteria = p.GetComponent<Porteria>();
            Transform breakGol = p.transform.Find("BreakGol");
            if (this.id % 2 != porteria.team % 2)
            {
                npcTarget = new Vector3(breakGol.position.x, transform.position.y, breakGol.position.z);

                dir = npcTarget - transform.position;
                break;
            }
         
        }

        dir.y = 0f;
        Vector3 velocity = dir.normalized * npcSpeed;
        rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);

        if (disparoPorteria)
        {
            if (Random.value < 0.005f) 
            {
                Shoot.instance.disparoLibre();
            }
        }

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
