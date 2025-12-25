using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class CharacterGV : MonoBehaviour
{
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

    private void Awake()
    {
        

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
        if (frozen && playerStop)
        {

            StartCoroutine(StopAndRetargetRoutine(playerStop));
            //Rigidbody rbPlayer = playerStop.GetComponent<Rigidbody>();
            //rbPlayer.linearVelocity = Vector3.zero;
            //rbPlayer.angularVelocity = Vector3.zero;
            
            // rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            //return;
        }
        movePlayer();
        othersBehaviour();
    }

    void movePlayer()
    {
        if (index != characterManager.index)
            return;

        float moveHorizontal = Input.GetAxis("Horizontal");
        float moveVertical = Input.GetAxis("Vertical");

        Vector3 movement = new Vector3(moveHorizontal, 0f, moveVertical) * speed;
        rb.linearVelocity = new Vector3(movement.x, rb.linearVelocity.y, movement.z);

        //if (Input.GetButtonDown("Jump"))
        //    rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
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

        StartCoroutine(ControlarTiempo());
    }

    private IEnumerator ControlarTiempo()
    {
        float distanciaTotal = 0f;
        Vector3 ultimaPosicion = transform.position;

        int segundos = 0;

        while (segundos < 2)
        {
            yield return new WaitForSeconds(1f);

            Vector3 posicionActual = transform.position;
            float distancia = Vector3.Distance(ultimaPosicion, posicionActual);

            distanciaTotal += distancia;
            ultimaPosicion = posicionActual;

            segundos++;
        }

        if (distanciaTotal < 15f)
        {
            PickNewTarget();
        }

    }

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
        // stopPlayerPass(player);
        frozen = true;
        playerStop = player;
    }

    //private void OnCharacterSelected(GameObject selectedPlayer)
    //{
    //    Pase.OnCharacterGVSelected
     
    //        stopPlayerPass(selectedPlayer);
       
    //}

    public void stopPlayerPass(GameObject selectedPlayer)
    {

        Rigidbody rbPlayer = selectedPlayer.GetComponent<Rigidbody>();
        rbPlayer.linearVelocity = Vector3.zero;
        rbPlayer.angularVelocity = Vector3.zero;
        //StartCoroutine(RecibirBola());
        //PickNewTarget();
    }

    private IEnumerator RecibirBola()
    {
        yield return new WaitForSeconds(2f);
    }

    private IEnumerator StopAndRetargetRoutine(GameObject playerStop)
    {
        Rigidbody rbPlayer = playerStop.GetComponent<Rigidbody>();

        
        rbPlayer.linearVelocity = Vector3.zero;
        rbPlayer.angularVelocity = Vector3.zero;

       

      
        yield return new WaitForSeconds(2f);

        // 🟢 NUEVO OBJETIVO
    //    PickNewTarget();

        // Reanudar movimiento
        frozen = false;
    }
}


