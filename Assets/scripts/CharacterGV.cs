using System.Collections;
using UnityEngine;

public class CharacterGV : PlayerID
{

    [SerializeField] private float turnSpeedDeg = 540f;     // velocidad de giro
    [SerializeField] private float directionLerp = 12f;     // suaviza cambios bruscos
    private Vector3 smoothDir = Vector3.forward;           // dirección suavizada
    private float speed = 150;
    private float changeTargetDistance = 50f;
    private Vector3 npcTarget;


    private Rigidbody rb;
    public int index;
    private CharacterManager characterManager;
   
    public static CharacterGV instance;

   

   
    private bool resetPos = false;

    

    public bool defaultMove = true;


    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        //instance = this;

    }

    private void Start()
    {
        characterManager = CharacterManager.instance;
 
    }

    private void Update()
    {
        movePlayer();
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
   
     

        //Si tiene bola y se cumple x probabilidad que se pase 

        
        //if (Bola.instance.transform.IsChildOf(transform))
        //{

        //    float aux = Random.value;
        //    if (aux < npcPass)
        //    {
        //        //Tendría que llamar al
        //        //Pase.instance.searchPlayersToPass("npc", transform.position, this.id);

        //        if (this.posicion == "Delantero")
        //        {
        //            Pase.instance.searchPlayersToPass("npc", transform.position, this.id);
        //        } else if (this.posicion == "CentroCampista")
        //        {
        //            Pase.instance.searchPlayersToPass("npc", transform.position, this.id);
        //        } else if (this.posicion == "Defensa")
        //        {
        //            Pase.instance.searchPlayersToPass("npc", transform.position, this.id);
        //            //Buscar a ese jugador su instancia y castearlo, algo similar al reset habría que hacer una función que busque su instancia 

        //        }
        //    }

        //}
     
    }



}


