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
  
    private CharacterManager characterManager;

    //Probabilidad de efectuar pase 
    private float npcPass = 0.002f; //PROVISIONAL

    public bool defaultMove = true;

    private float moveInArea = 0.000005f;

    private Arbitro arbitro;

    private AreaPeligro ap;
    private MedioCampo med;
  

    private int areaPropia;
    private int areaContraria;
    private int medioCampo;

    private int contrariosInArea = 0;
 
  
    private int areaPlayer;
    private int areaSmall;
    private bool chasingBall = false;
    private bool disparoPorteria = false;
    private ZoneManager zoneManager;

    private float refreshInterval = 0.5f;
    private float nextRefreshTime;

    float nextDecisionTime;
    float decisionInterval = 1f;

    float tiroTime;
    float tiroInterval = 0.5f;

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

        if (frozen && playerStop)
        {
            StartCoroutine(StopRoutine(playerStop));
        }

        if (resetPos)
            return;

        cerebro();
        changeSpeed();

        if (stop)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        int miEquipo = this.id % 2;
        bool bolaEnMiArea = areaConBola == miEquipo;
        bool bolaEnOtraArea = areaConBola != -1 && areaConBola != miEquipo;
        bool bolaSinArea = areaConBola == -1;
        bool tengoBola = Bola.instance.transform.IsChildOf(transform);

        // =========================
        // SI TIENE LA BOLA
        // =========================
        if (tengoBola)
        {
            chasingBall = false;

            // Movimiento con balón: una sola orden
            move("otherArea");

            // Decisiones con balón
            // El jugador que lleva el humano decide con el teclado (O y P).
            // Sin esta guarda el rol seguia ejecutando sus propios pases: dentro
            // del bloque solo estaban protegidos el PaseConCriterio de Tirar() y
            // el tiroPorteria, pero no passBallOwnPlayer ni los otros dos
            // PaseConCriterio, asi que el centrocampista controlado pasaba solo.
            // Delantero y Defensa ya lo hacian bien; este era el unico rol con
            // el agujero, y es justo el del "centro ofensivo".
            if (this.id == characterManager.index) return;

            if (AccionConBola())
            {
                float decision = Random.value;
                bool enAreaChica = areaSmall != -1;


                if (enAreaChica && bolaEnOtraArea)
                {
                    if (Tirar())
                    {
                        if (characterManager.index != this.id && Random.value < 0.5f)
                        {
                            PaseConCriterio();
                        }
                    }
                }


                bool hacerPase = Random.value < 0.5f;

                if (hacerPase)
                {
                    if (decision < 0.05f)
                    {
                        opcionPaseLoco();
                    }
                    else if (decision < 0.3f)
                    {
                        if (Pase.instance.calculateDistance(this.transform.position) < 200)
                        {
                            Pase.instance.passBallOwnPlayer();
                        }
                        else
                        {
                            PaseConCriterio();
                        }

                    }
                    else if (decision < 0.6f)
                    {
                        PaseConCriterio();
                    }
                    else if (decision < 0.95f)
                    {
                        disparoPorteria = true;
                    }
                }

                if (disparoPorteria && Random.value < 0.1f)
                {
                    disparoPorteria = false;

                    if (characterManager.index != this.id)
                    {
                        tiroPorteria();
                    }
                }
            }

            return;
        }

        // =========================
        // SI NO TIENE LA BOLA
        // =========================

        bool perseguir = false;

        if (bolaEnOtraArea)
        {
            perseguir = false;
        }
        else if (bolaEnMiArea)
        {
            if (ChangeChasingBallTrue())
            {
                chasingBall = Random.value < 0.5f;
            }

            perseguir = chasingBall || getChasingBallFree(this);
        }
        else if (bolaSinArea)
        {
            perseguir = getChasingBallFree(this);
        }

        // Una sola orden final de movimiento
        if (perseguir)
        {
            runToBall();
        }
        else
        {
            move("nearBall");
        }
    }

    void FixedUpdate()
    {
        rotacion();
    }



    protected void runToBall()
    {
        if (this.id == characterManager.index) return;
        if (stop)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

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


    protected bool Tirar()
    {
        if (Time.time < tiroTime)
            return false;

        tiroTime = Time.time + tiroInterval;
        return true;
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



    private void move(string targetTag)
    {

        if (this.id == characterManager.index) return;

        if (stop)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        if (targetTag != this.targetTag)
        {
            this.targetTag = targetTag;
            npcTarget = Vector3.zero;
            PickNewTarget();
        }

        Vector3 direction = (npcTarget - transform.position).normalized;

           float currentSpeed = npcSpeed;

        if (Bola.instance.transform.IsChildOf(this.transform))
        {
            currentSpeed *= 0.85f;
        }


        rb.linearVelocity = new Vector3(
            direction.x * currentSpeed,
            rb.linearVelocity.y,
            direction.z * currentSpeed
        );

        if (Vector3.Distance(transform.position, npcTarget) < changeTargetDistance)
            PickNewTarget();
    }


        void PickNewTarget()
    {
        if (this.targetTag == "otherArea" || this.targetTag == "")
        {
            float x = 0, z = 0;
            GameObject[] areas = GameObject.FindGameObjectsWithTag("AreaPeligro");

            foreach (GameObject a in areas)
            {
                AreaPeligro area = a.GetComponent<AreaPeligro>();

                if (area != null && area.TEAM != this.id % 2)
                {
                    minX = area.minX;
                    maxX = area.maxX;
                    break;
                } 
            }

            x = Random.Range(minX, maxX);
            z = Random.Range(field.minZ, field.maxZ);

            npcTarget = new Vector3(x, transform.position.y, z);
        }
        else if (this.targetTag == "nearBall")
        {
            goNearBall();
        }
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

    void OnSelected(PlayerID player)
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


    public override bool EnReset { get { return resetPos; } }

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
        yield return new WaitForSeconds(4f);
        resetPos = false;
    }

    public void setPase(bool pase)
    {
        this.pase = pase;
    }

    public IEnumerator PararJugador(PlayerID target)
    {
        stop = true;
        playerStop = target;

        Rigidbody rbPlayer = playerStop.GetComponent<Rigidbody>();
        if (rbPlayer != null)
        {
            rbPlayer.linearVelocity = Vector3.zero;
            rbPlayer.angularVelocity = Vector3.zero;
        }

        yield return new WaitForSeconds(2f);
        stop = false;
        playerStop = null;
    }
}
