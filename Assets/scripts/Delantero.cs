using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.LowLevel;
using Random = UnityEngine.Random;

public class Delantero : PlayerID, IResettable
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
    private float npcPass = 0.1f; //PROVISIONAL

    public bool defaultMove = true;

    private float moveInArea = 0.95f;
    private AreaPeligro ap;
                                    // Ir hacia porteria y tirar si no se la roban 

    private bool disparoPorteria = false;
    private bool yendoAAreaRival = false;
    private int areaSmall;

    float tiroTime;
    float tiroInterval = 0.5f;
    protected override void Awake()
    {
        base.Awake();

        spawnPos = transform.position;
        spawnRot = transform.rotation;
    }

    void Start()
    {
        characterManager = CharacterManager.instance;
        field = FieldLimits.instance;
        ap = AreaPeligro.instance;
        //PickNewTarget(); 
    }

    /*
     Si tienen la bola que se acerque a la portería y apunte, también que la pase a un medio campo, o que se pueda a equivocar 
     */

    void Update()
    {
        whereIsBall();
        areaSmall = IsInSmallArea();

        int miEquipo = this.id % 2;
        bool enAreaChica = areaSmall != -1;
        bool bolaEnMiArea = areaConBola == miEquipo;
        bool bolaEnOtraArea = areaConBola != -1 && areaConBola != miEquipo;
        bool bolaSinArea = areaConBola == -1;

        if (resetPos) return;
        if (frozen && playerStop) StartCoroutine(StopRoutine(playerStop));

        changeSpeed();

        if (chasingBallFree)
        {
            runToBall();
            return;
        }

        if (stop)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        // Solo actúa si la bola está en el área del mismo equipo que este delantero
        if (areaConBola == this.id % 2)
        {
            disparoPorteria = false;
            if (!yendoAAreaRival && !stop)
            {
                goToOtherAreaForward();
                yendoAAreaRival = true;
            }

            if (!stop)
            {
                move("otherArea");
            }

            if (Vector3.Distance(transform.position, npcTarget) < 5f)
            {
                Parar();
            }
        }
        else if (areaConBola != this.id % 2)
        {
            disparoPorteria = false;
            if (IsInSmallArea() != this.id % 2)
            {
                disparoPorteria = true;
            }
            move("otherArea");
        }
        else
        {
            disparoPorteria = false;
            move("otherArea");
        }

        if (Bola.instance.transform.IsChildOf(transform))
        {

            if (enAreaChica && bolaEnOtraArea)
            {
                if (Tirar())
                {
                    if (characterManager.index == this.id) return;
                    if (Random.value < 0.75f)
                    {
                        PaseConCriterio();
                    }
                }
            }
            else
            {
                if (PuedoTirar())
                {
                    opcionPase();
                }

                move("otherArea");
            }

        }
        else
        {
            disparoPorteria = false;
        }
    }

    void FixedUpdate()
    {
        rotacion();
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

    protected void runToBall()
    {
        Vector3 dir = Bola.instance.transform.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.001f) return;

        Vector3 velocity = dir.normalized * npcSpeed;
        rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);
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


        rb.linearVelocity = new Vector3(
            direction.x * currentSpeed,
            rb.linearVelocity.y,
            direction.z * currentSpeed
        );

        if (Vector3.Distance(transform.position, npcTarget) < changeTargetDistance)
            PickNewTarget();
    }

    private void opcionPase()
    {
        if (this.id == characterManager.index) { return; }

        if (disparoPorteria)
        {
            tiroPorteria();
            return;
        }

        if (Random.value < npcPass)
        {
            float aux = Random.value;
            if (aux < 0.1f)
            {
                Shoot.instance.disparoLibre();
            }
            else if (0.1f < aux && aux < 0.2f)
            {
                Pase.instance.searchPlayersToPass("npc", transform.position, this.id);
            }
            else if (0.2f < aux && aux < 0.45f)
            {
                PaseConCriterio();
            }
            else if (0.45f < aux && aux < 0.7f)
            {
                tiroPorteria();
            }
            else if (0.7f < aux && aux < 0.9f) { Pase.instance.passBallOwnPlayer(); }

        }
        
    }

    void PickNewTarget()
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

    protected bool Tirar()
    {
        if (Time.time < tiroTime)
            return false;

        tiroTime = Time.time + tiroInterval;
        return true;
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

    public void activarTiro()
    {
        disparoPorteria = true;
    }

    public void desactivarTiro()
    {
        disparoPorteria = false;
    }

    //private void tiroPorteria()
    //{
    //    if (characterManager.index == this.id) return;
    //    GameObject[] porterias = GameObject.FindGameObjectsWithTag("Porteria");
    //    Vector3 dir = new Vector3(0,0);

    //    foreach (GameObject p in porterias)
    //    {
    //        Porteria porteria = p.GetComponent<Porteria>();
    //        Transform breakGol = p.transform.Find("BreakGol");
    //        if (this.id % 2 != porteria.team % 2)
    //        {
    //            npcTarget = new Vector3(breakGol.position.x, transform.position.y, breakGol.position.z);

    //            dir = npcTarget - transform.position;
    //            break;
    //        }
         
    //    }

    //    dir.y = 0f;
    //    Vector3 velocity = dir.normalized * npcSpeed;
    //    rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);

    //    if (disparoPorteria)
    //    {
    //        if (Random.value < 0.005f) 
    //        {
    //            Shoot.instance.disparoLibre();
    //        }
    //    }

    //}


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

    void goToOtherAreaForward()
    {
        int areaSet = (this.id % 2 == 0) ? 1 : 0;
        ZoneManager.instance.getAreasByTeam(areaSet);
        List<ZoneManager.ZonaBola> areas = ZoneManager.instance.getAreasByTeam(areaSet);
        if (areas == null || areas.Count == 0)
            return;

        Vector3 randomPoint = ZoneManager.instance.GetRandomPointInSelectedAreas(areas, transform.position.y);

        if (randomPoint != Vector3.zero)
        {
            npcTarget = randomPoint;
        }
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
