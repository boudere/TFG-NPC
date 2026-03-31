using System;
using System.Collections;
using UnityEngine;
using UnityEngine.LowLevel;
using Random = UnityEngine.Random;

public class Defensa : PlayerID, IResettable
{


    private Vector3 spawnPos;
    private Quaternion spawnRot;
    private bool resetPos = false;
    public bool pase = false;
    private FieldLimits field;
    private AreaPeligro ap;

    [SerializeField] private float turnSpeedDeg = 540f;     // velocidad de giro
    [SerializeField] private float directionLerp = 12f;     // suaviza cambios bruscos
    private Vector3 smoothDir = Vector3.forward;           // dirección suavizada
    private float speed = 150;
    private float npcSpeed = 75;
    private float changeTargetDistance = 50f;
    private Vector3 npcTarget;


    private CharacterManager characterManager;

    [System.Serializable]
    public class Marcaje
    {
        public Delantero delantero;
        public Defensa defensa;

        public Marcaje(Delantero del, Defensa def)
        {
            delantero = del;
            defensa = def;
        }
    }

    public Marcaje marcajeActual;


    //Probabilidad de efectuar pase 
    private float npcPass = 0.02f; //PROVISIONAL

    //Probabilidades (luego pueden fallarse o no) 

    //Tirarla libre
    //Tirarla a un jugador
    //Tirarla a un centro del campo 

    /*
     *  Tiro libre De 0 a 30
     *  Tiro a cualquier jugador 30 a 70
     *  Tiro a un medio campo 70 a 100
     */

    private float tiroCase1 = 0.3f;
    private float tiroCase2 = 0.7f;
 



    public bool defaultMove = true;


    private float moveInArea = 0.95f;

    public bool defender = false;
    private float tiempoMarcar = 0f;
    private Transform targetDelantero;
    private bool puedePerseguir = false;

    /*
     En caso de que se pueda dar asistencia ...
     */


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
        PickNewTarget();
    }


    void Update()
    {
        //if (frozen && playerStop)
        //{
        //    StartCoroutine(StopAndRetargetRoutine(playerStop));
        //}

        //if (resetPos)
        //{
        //    return;
        //}

        //if (defender && !Bola.instance.transform.IsChildOf(transform) && puedePerseguir)
        //{
        //    defenderJug();
        //    return;
        //}

        //if (chasingBallFree)
        //{

        //    runToBall();
        //}
        //else
        //{
        //    move();
        //}


        //if (Bola.instance.transform.IsChildOf(transform))
        //{
        //    opcionPase();
        //}


        // Perseguir a delantero si está en área de defensa 
        /*
         Lo voy a gestionar desde los triggers de los areas, si entra en area peligro habrá una probabilidad menor de que le "siga" que si entra en area de gol

         */

        // Contar jugadores por campo y si hay más en el otro ir  hacia allá  (De momento no lo hago)


    }

    void FixedUpdate()
    {
        rotacion();
    }

    private float tiempoIrMarca()
    {
        float aux = Random.value;

        if (aux < 0.25) {
            return 0.5f;
        } else if (aux > 0.25 && aux < 0.5) {
            return 1f;
        } else if (aux > 0.5 && aux < 0.75) {
            return 1.5f;
        } else {
            return 2f;
        }

    }

    private IEnumerator EsperarParaPerseguir()
    {
        yield return new WaitForSeconds(tiempoMarcar);
        puedePerseguir = true;
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


    private void move()
    {

        if (this.id == characterManager.index) { return; }

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

    private void opcionPase()
    {

        if (this.id == characterManager.index) { return; }

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
                Pase.instance.searchPlayersToPass("CentroCampista", transform.position, this.id);
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

                if (area != null && area.TEAM == this.id % 2)
                {
                    x = Random.Range(area.minX, area.maxX);
                    z = Random.Range(area.minZ, area.maxZ);
                }
            }
         
        } else
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

    private IEnumerator StopAndRetargetRoutine(GameObject playerStop)
    {
        Rigidbody rbPlayer = playerStop.GetComponent<Rigidbody>();
        rbPlayer.linearVelocity = Vector3.zero;
        rbPlayer.angularVelocity = Vector3.zero;
        yield return new WaitForSeconds(2f);
        frozen = false;
    }

    public void activarDefensa(Transform target)
    {
        defender = true;
        targetDelantero = target;
        puedePerseguir = false;
    }

  
    private void defenderJug()
    {

        if (targetDelantero == null)
        {
            DejarDeDefender();
            return;
        }

        float distanciaDefensa = 20f;

        Vector3 dir = (targetDelantero.position - transform.position);
        dir.y = 0f;

        float sqrDist = dir.sqrMagnitude;
        float sqrDistanciaDefensa = distanciaDefensa * distanciaDefensa;

        if (sqrDist <= sqrDistanciaDefensa)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);

            if (sqrDist > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, 10f * Time.deltaTime));
            }
            return;
        }
        Vector3 velocity = dir.normalized * npcSpeed;
        rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);

        Quaternion rot = Quaternion.LookRotation(dir);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, rot, 10f * Time.deltaTime));

        //if () // POSICION
        /*
         Si le ha robado la pelota al jugador, que se noquee
         */


    }

    public void DejarDeDefender()
    {
        defender = false;
        tiempoMarcar = 0f;
        targetDelantero = null;

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

    public void setPase(bool pase)
    {
        this.pase = pase;
    }


    public void AsignarMarcaje(Delantero d)
    {
        if (d == null) return;

        tiempoMarcar = tiempoIrMarca();
        StartCoroutine(EsperarParaPerseguir());

        marcajeActual = new Marcaje(d, this);
        this.setLibre(false);
        activarDefensa(d.transform);


        Transform area = this.player.transform.Find("area");
        CapsuleCollider col = area.GetComponent<CapsuleCollider>();

        col.radius *= 1.3f;
        Transform canvas = transform.Find("shoot/Canvas/Image");
        RectTransform imageRect = canvas.GetComponentInChildren<RectTransform>();
        imageRect.localScale *= 1.3f;

    }

    public void QuitarMarcaje(Delantero d)
    {
        if (marcajeActual == null) return;
        if (marcajeActual.delantero != d) return;

        Transform area = this.player.transform.Find("area");
        CapsuleCollider col = area.GetComponent<CapsuleCollider>();

        col.radius /= 1.3f;
        Transform canvas = transform.Find("shoot/Canvas/Image");
        RectTransform imageRect = canvas.GetComponentInChildren<RectTransform>();

        imageRect.localScale /= 1.3f;

        marcajeActual = null;
        DejarDeDefender();
        this.setLibre(true);
    }


}
