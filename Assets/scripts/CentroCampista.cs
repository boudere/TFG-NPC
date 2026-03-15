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
    public bool pase = false;
    private FieldLimits field;

    [SerializeField] private float turnSpeedDeg = 540f;     // velocidad de giro
    [SerializeField] private float directionLerp = 12f;     // suaviza cambios bruscos
    private Vector3 smoothDir = Vector3.forward;           // dirección suavizada
    private float speed = 150;
  
    private float changeTargetDistance = 50f;
    private Vector3 npcTarget;

    private CharacterManager characterManager;

    //Probabilidad de efectuar pase 
    private float npcPass = 0.002f; //PROVISIONAL

    public bool defaultMove = true;

    private float moveInArea = 0.000005f;

    private Arbitro arbitro;

    private AreaPeligro ap;
    private MedioCampo med;
    private float maxX, minX;

    private int areaPropia;
    private int areaContraria;
    private int medioCampo;

    private int contrariosInArea = 0;
    private int areaConBola;
    private int areaPlayer;

    //IR A POR LA BOLA
    //private bool runTowardsBall = false;
   
    private float passBallCase1 = 0.2f, passBallCase2 = 0.40f, passBallCase3 = 0.70f; // 1: Disparo random, 2: Disparo a cualquiera, 3: Disparo a otro centro, 4:Disparo a delantero


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
        med = MedioCampo.instance;
        arbitro = Arbitro.instance;
        PickNewTarget(); 
    }


    void Update()
    {
        whereIsBall();
        areaPlayer = whereIsPlayer();
        if (frozen && playerStop)
        {
            StartCoroutine(StopAndRetargetRoutine(playerStop));
        }

        if (resetPos)
        {
            return;
        }


        cerebro();

        if (Bola.instance.transform.IsChildOf(transform))
        {
            chasingBall = false;

            if (Random.value < 0.05)
            {

                if (areaPlayer == -1 || areaPlayer == this.id % 2)
                {
                    if (Random.value < 0.005)
                    {
                        opcionPase();
                    }
                }
                else
                {

                    opcionPase();
                }
            }
            else
            {
                goToOtherArea();
            }

            move();

        }
        else
        {

            if (areaConBola == this.id % 2 && arbitro.idTeamBola() != this.id % 2)
            {
                if (!chasingBall && Random.value < 0.0005f)
                {
                    chasingBall = true;
                }

                if (chasingBall)
                    runToBall();
                else
                    move();
            }
            else if (areaConBola != -1 && areaConBola != this.id % 2)
            {
                chasingBall = false;
                goToOtherArea();
                move();
            }
            else if (areaConBola == -1)
            {
                if (!chasingBall && Random.value < 0.0005f)
                {
                    chasingBall = true;
                }

                if (chasingBall)
                {
                    if (Random.value < 0.0001f)
                    {
                        chasingBall = false;
                    }
                    else
                    {
                        runToBall();
                    }
                }
                else
                {
                    move();
                }
            }
            else
            {
                move();
            }
        }
    }

    void FixedUpdate()
    {
        rotacion();
    }

    void rotacion()
    {
        if (this.id == characterManager.index) return;
        if (this.id % 2 == 1) return;

        Vector3 dir;

        if (chasingBall)
            dir = Bola.instance.transform.position - transform.position;
        else
            dir = npcTarget - transform.position;

        dir.y = 0f;

        if (dir.sqrMagnitude <= 0.001f) return;

        dir.Normalize();

        smoothDir = Vector3.Slerp(smoothDir, dir, directionLerp * Time.fixedDeltaTime);

        Quaternion targetRot = Quaternion.LookRotation(smoothDir, Vector3.up);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, turnSpeedDeg * Time.fixedDeltaTime));
    }

    public void whereIsBall()  // SABER DONDE ESTA LA BOLA
    {
        areaConBola = -1;
        GameObject[] areas = GameObject.FindGameObjectsWithTag("AreaPeligro");
        foreach (GameObject a in areas)
        {
            AreaPeligro area = a.GetComponent<AreaPeligro>();
            if (area.isBallInArea())
            {
                areaConBola = area.TEAM;
                break;
            }
        }
    }

    public int whereIsPlayer() {  // SABER DONDE ESTA EL PLAYER
        GameObject[] areas = GameObject.FindGameObjectsWithTag("AreaPeligro");
        foreach (GameObject a in areas)
        {
            AreaPeligro area = a.GetComponent<AreaPeligro>();
            if (area.isPlayerInArea(this.id))
            {
                return area.TEAM;
              
            }
        }

        return -1;
    }

    private void cerebro()
    {
        int team = this.id % 2;
        medioCampo = med.getPlayersInMedioCampo();
      

        GameObject[] areas = GameObject.FindGameObjectsWithTag("AreaPeligro");
        foreach (GameObject a in areas)
        {
            AreaPeligro area = a.GetComponent<AreaPeligro>();

            if (this.id % 2 == area.TEAM)
            {

                areaPropia = area.getPlayersInArea();
                areaContraria = area.getPlayersInArea();
                if (team == 0)
                {
                    contrariosInArea = area.getPlayersByTeam(1);
                } else if (team == 1)
                {
                    contrariosInArea = area.getPlayersByTeam(0);
                }
                
                break;
            }


        }
    }

    private void move()
    {
       

        if (this.id == characterManager.index) return;

        if (stop)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

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


        float x = 0, z = 0;
        if (Random.value < moveInArea)
        {
            x = Random.Range(field.minX, field.maxX);
            z = Random.Range(field.minZ, field.maxZ);
        }
        else
        {

            GameObject[] areas = GameObject.FindGameObjectsWithTag("AreaPeligro");

            foreach (GameObject a in areas)
            {
                AreaPeligro area = a.GetComponent<AreaPeligro>();

                if (area != null && area.TEAM == 1)
                { 
                    minX = area.minX;
                }
                else
                {
                     maxX = area.maxX;
                }
            }


            x = Random.Range(minX, maxX);
            z = Random.Range(field.minZ, field.maxZ);
        }

            npcTarget = new Vector3(x, transform.position.y, z);
    }

    private void opcionPase()
    {

        if (this.id == characterManager.index) { return; }
  
        float aux = Random.value;
        if (aux < passBallCase1)
        {
            Shoot.instance.disparoLibre();
        }
        else if (passBallCase1 < aux && aux < passBallCase2)
        {
            Pase.instance.searchPlayersToPass("npc", transform.position, this.id);
        }
        else if (passBallCase2 < aux && aux < passBallCase3) // Que estén en el area
        {
            Pase.instance.searchPlayersToPass("CentroCampista", transform.position, this.id);
        }
        else
        {
            Pase.instance.searchPlayersToPass("Delantero", transform.position, this.id);
        }
    }

    private void goToOtherArea()
    {

        if (Vector3.Distance(transform.position, npcTarget) > changeTargetDistance)
            return;

        GameObject[] areas = GameObject.FindGameObjectsWithTag("AreaPeligro");

        float x = transform.position.x;
        float z = transform.position.z;

        foreach (GameObject a in areas)
        {
            AreaPeligro area = a.GetComponent<AreaPeligro>();

            if (area != null && area.TEAM != this.id % 2) // área contraria
            {
                x = Random.Range(area.minX, area.maxX);
                z = Random.Range(area.minZ, area.maxZ);
                break;
            }
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
