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
    private Rigidbody rb;
    public bool pase = false;
    private FieldLimits field;
    private AreaPeligro1 ap1;
    private AreaPeligro2 ap2;

    [SerializeField] private float turnSpeedDeg = 540f;     // velocidad de giro
    [SerializeField] private float directionLerp = 12f;     // suaviza cambios bruscos
    private Vector3 smoothDir = Vector3.forward;           // dirección suavizada
    private float speed = 150;
    private float npcSpeed = 75;
    private float changeTargetDistance = 50f;
    private Vector3 npcTarget;

    private CharacterManager characterManager;

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

    private bool frozen = false;
    private GameObject playerStop = null;

    private float moveInArea = 0.5f;

    public bool defender = false;
    private Transform targetDelantero;


    /*
     En caso de que se pueda dar asistencia ...
     */


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

    
    void Update()
    {

        if (defender && !Bola.instance.transform.IsChildOf(transform))
        {
            defenderJug();
            return;
        }

        if (frozen && playerStop)
        {
            StartCoroutine(StopAndRetargetRoutine(playerStop));
        }

        if (resetPos)
        {
            return;
        }


        move(); 

        if (Bola.instance.transform.IsChildOf(transform))
        {
            opcionPase();
        }

        // Perseguir a delantero si está en área de defensa 
        /*
         Lo voy a gestionar desde los triggers de los areas, si entra en area peligro habrá una probabilidad menor de que le "siga" que si entra en area de gol
         
         */
         
        // Contar jugadores por campo y si hay más en el otro ir  hacia allá  (De momento no lo hago)

      
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

    private void opcionPase()
    {
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
        float x, z;
        if (Random.value < moveInArea)
        {

            
            if (this.id % 2 == 0)
            {
                x = Random.Range(ap1.minX, ap1.maxX);
                z = Random.Range(ap1.minZ, ap1.maxZ);

            } else
            {
                x = Random.Range(ap2.minX, ap2.maxX);
                z = Random.Range(ap2.minZ, ap2.maxZ);
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
        // Si detecta delanteros en el area -> Olvida su target 
        defender = true;
        targetDelantero = target;
    }

    private void defenderJug()
    {
        if (targetDelantero == null)
        {
            DejarDeDefender();
            return;
        }

        Vector3 dir = (targetDelantero.position - transform.position);
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.01f)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

      
        Vector3 velocity = dir.normalized * npcSpeed;
        rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);

        Quaternion targetRot = Quaternion.LookRotation(dir);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, 10f * Time.deltaTime));
    }

    public void DejarDeDefender()
    {
        defender = false;
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
}
