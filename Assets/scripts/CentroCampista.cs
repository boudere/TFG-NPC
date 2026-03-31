using System;
using System.Collections;
using System.Collections.Generic;
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

    //public struct FranjaBola{
    //   public int franja;
    //   public int team;
    //}

    //public struct SideBola{
    //    public int side;
    //    public int team;
    //}

    

    //public struct ZonaBola
    //{
    //    public int zona;
    //    public int team;
    //}

    //ZonaBola franjaBola;
    //ZonaBola sideBola;

    private int areaPlayer;
    private int areaSmall;
    private bool chasingBall = false;
    private bool disparoPorteria = false;
    private ZoneManager zoneManager;

    //IR A POR LA BOLA
    //private bool runTowardsBall = false;


    private float refreshInterval = 0.5f;
    private float nextRefreshTime;

    float nextDecisionTime;
    float decisionInterval = 1f;

    float nextTiroBolaTime;
    float tiroBolaInterval = 1f;


    protected override void Awake()
    {
        base.Awake();

        spawnPos = transform.position;
        spawnRot = transform.rotation;
        zoneManager = ZoneManager.instance;
    }

    void Start()
    {
        characterManager = CharacterManager.instance;
        field = FieldLimits.instance;
        ap = AreaPeligro.instance;
        med = MedioCampo.instance;
        arbitro = Arbitro.instance;
        zoneManager = ZoneManager.instance;
        PickNewTarget(); 
    }


    void Update()
    {
        whereIsBall();
        areaPlayer = whereIsPlayer();
        areaSmall = IsInSmallArea();
        //Hacer otra función para el area pequeña
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

            // Solo decide cada 1 segundo
            if (AccionConBola())
            {
                float decision = Random.value;
                bool intentarPase = Random.value < 0.3f;
                bool estaEnZonaPermitida = areaPlayer == -1 || areaPlayer == this.id % 2;
                bool enAreaChica = areaSmall != -1;

                if (enAreaChica)
                {
                    //Disparar a porteria en x segundos
                    if (PuedoTirar())
                    {
                        tiroPorteria();
                    }
                }

                if (intentarPase)
                {
                    bool hacerPase = !estaEnZonaPermitida || Random.value < 0.005f;

                   if (decision < 0.1f)
                    {
                        opcionPaseLoco();
                    } else if (decision > 0.1f && decision < 0.4f)
                    {
                        //Pase con criterio
                        PaseConCriterio();

                    } else if (decision > 0.4f && decision < 0.7f)
                    {
                        //Disparar a porteria 
                        disparoPorteria = true;
                    } 
                }

                if (disparoPorteria && Random.value < 0.1f)
                {
                    tiroPorteria();
                    disparoPorteria = false;
                }

            }

            goToOtherArea();
            move();
        }
        else
        {
            int miEquipo = this.id % 2;
            bool bolaEnMiArea = areaConBola == miEquipo;
            bool bolaEnOtraArea = areaConBola != -1 && areaConBola != miEquipo;
            bool bolaSinArea = areaConBola == -1;

            if (bolaEnOtraArea)
            {
                goNearBall();
                move();
            }
            else if ((bolaEnMiArea && arbitro.idTeamBola() != miEquipo) || bolaSinArea)
            {
                
                if (ChangeChasingBallTrue())
                {
                    if (Random.value < 0.25f) { chasingBall = true; }
                }
                

                if (chasingBall)
                {
                    runToBall();
                }
                else
                {
                    goNearBall();
                    move();
                }
            }
            else
            {
                move();
            }


            bool perseguir = getChasingBallFree(this);


            if (perseguir)
            {
                runToBall();

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

    protected void runToBall()
    {
        Vector3 dir = Bola.instance.transform.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.001f) return;

        Vector3 velocity = dir.normalized * npcSpeed;
        rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);
    }

    void rotacion()
    {
        if (this.id == characterManager.index) return;
      

        Vector3 dir = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (dir.sqrMagnitude > 0.01f)
        {
            dir.Normalize();
            smoothDir = Vector3.Slerp(smoothDir, dir, directionLerp * Time.fixedDeltaTime);

            Quaternion targetRot = Quaternion.LookRotation(smoothDir, Vector3.up);
            rb.MoveRotation(
                Quaternion.RotateTowards(rb.rotation, targetRot, turnSpeedDeg * Time.fixedDeltaTime)
            );
            return;
        }

        dir = npcTarget - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude <= 0.001f) return;

        dir.Normalize();
        smoothDir = Vector3.Slerp(smoothDir, dir, directionLerp * Time.fixedDeltaTime);

        Quaternion idleTargetRot = Quaternion.LookRotation(smoothDir, Vector3.up);
        rb.MoveRotation(
            Quaternion.RotateTowards(rb.rotation, idleTargetRot, turnSpeedDeg * Time.fixedDeltaTime)
        );
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



    void PaseConCriterio()
    {

    }

    bool ChangeChasingBallTrue()
    {
        if (Time.time < nextRefreshTime)
            return false;

        nextRefreshTime = Time.time + refreshInterval;
        return true;
    }

    bool AccionConBola()
    {
        if (Time.time < nextDecisionTime)
            return false;

        nextDecisionTime = Time.time + decisionInterval;
        return true;
    }

    bool PuedoTirar()
    {
        if (Time.time < nextTiroBolaTime)
            return false;

        nextDecisionTime = Time.time + tiroBolaInterval;
        return true;
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


    private void tiroPorteria()
    {
        GameObject[] porterias = GameObject.FindGameObjectsWithTag("Porteria");
        Vector3 dir = new Vector3(0, 0);

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

        Shoot.instance.disparoLibre();

    }
    private void opcionPaseLoco()
    {

        if (this.id == characterManager.index) { return; }
  
        float aux = Random.value;
        if (aux < 0.1f)
        {
            Shoot.instance.disparoLibre();
        }
        else if (0.1f < aux && aux < 0.4f)
        {
            Pase.instance.searchPlayersToPass("npc", transform.position, this.id);
        }
        else if (0.4f < aux && aux < 0.7f) // Que estén en el area
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

    private void goNearBall()
    {
        if (Vector3.Distance(transform.position, npcTarget) > changeTargetDistance)
            return;

        List<ZoneManager.ZonaBola> areas = ZoneManager.instance.getAreasAdyacentes();
        if (areas == null || areas.Count == 0)
            return;

        Vector3 randomPoint = ZoneManager.instance.GetRandomPointInSelectedAreas(areas, transform.position.y);

        if (randomPoint != Vector3.zero)
        {
            npcTarget = randomPoint;
        }
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
