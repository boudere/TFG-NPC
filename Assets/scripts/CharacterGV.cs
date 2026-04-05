using System.Collections;
using UnityEngine;
using UnityEngine.LowLevel;

public class CharacterGV : PlayerID
{

    [SerializeField] private float turnSpeedDeg = 540f;     // velocidad de giro
    [SerializeField] private float directionLerp = 12f;     // suaviza cambios bruscos
    private Vector3 smoothDir = Vector3.forward;           // dirección suavizada
    private float speed = 150;

    public int index;
    private CharacterManager characterManager;
   
    public static CharacterGV instance;




    public bool defaultMove = true;


    private void Start()
    {
        characterManager = CharacterManager.instance;
 
    }

    private void Update()
    {

        if (frozen && playerStop)
        {
            StartCoroutine(StopRoutine(playerStop));
        }

        movePlayer();
        
    }
    void movePlayer()
    {
        if (stop)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        if (index != characterManager.index)
            return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 inputDir = new Vector3(h, 0f, v);

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


