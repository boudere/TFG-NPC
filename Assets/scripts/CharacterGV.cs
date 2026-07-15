using System.Collections;
using UnityEngine;

public class CharacterGV : PlayerID
{
    [SerializeField] private float turnSpeedDeg = 540f;
    [SerializeField] private float directionLerp = 12f;
    [SerializeField] private float sprintSpeed = 300f;
    [SerializeField] private float sprintMaxDistance = 400f;

    private Vector3 smoothDir = Vector3.forward;

    public int index;
    private CharacterManager characterManager;

    private bool resetPos;

    public bool sprint;
    private Vector3 sprintDirection;
    private Vector3 sprintInitPosition;
    private Vector3 sprintTargetPosition;

    public bool defaultMove = true;

    private void Start()
    {
        characterManager = CharacterManager.instance;
    }

    private void Update()
    {
        if (resetPos)
            return;

        changeSpeed();

        if (frozen && playerStop)
        {
            StartCoroutine(StopRoutine(playerStop));
        }

        MovePlayer();
    }

    private void MovePlayer()
    {
        if (stop)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        if (index != characterManager.index)
            return;

        Vector3 moveDir;

        if (sprint)
        {
            float distanciaRecorrida =
                Vector3.Distance(transform.position, sprintInitPosition);

            float distanciaAlObjetivo =
                Vector3.Distance(transform.position, sprintTargetPosition);

            if (distanciaRecorrida >= sprintMaxDistance ||
                distanciaAlObjetivo <= 5f)
            {
                StopSprint();
                return;
            }

            moveDir = sprintDirection;
        }
        else
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            moveDir = new Vector3(h, 0f, v).normalized;
        }

        float speed = sprint ? sprintSpeed : npcSpeed;

        Vector3 movement = moveDir * speed;

        rb.linearVelocity = new Vector3(
            movement.x,
            rb.linearVelocity.y,
            movement.z
        );

        if (moveDir.sqrMagnitude > 0.001f)
        {
            smoothDir = Vector3.Slerp(
                smoothDir,
                moveDir,
                directionLerp * Time.deltaTime
            );

            Quaternion targetRot =
                Quaternion.LookRotation(smoothDir, Vector3.up);

            rb.MoveRotation(
                Quaternion.RotateTowards(
                    rb.rotation,
                    targetRot,
                    turnSpeedDeg * Time.deltaTime
                )
            );
        }
    }

    public void SetSprint(Vector3 targetPosition)
    {
        // Se guardan una sola vez al pulsar K.
        sprintInitPosition = transform.position;
        sprintTargetPosition = targetPosition;

        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
            return;

        sprintDirection = direction.normalized;
        sprint = true;
    }

    private void StopSprint()
    {
        sprint = false;
        sprintDirection = Vector3.zero;

        rb.linearVelocity = new Vector3(
            0f,
            rb.linearVelocity.y,
            0f
        );
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

        yield return new WaitForSeconds(0.5f);

        stop = false;
        playerStop = null;
    }

    public void ResetToSpawn()
    {
        resetPos = true;

        rb.linearVelocity =
            new Vector3(0f, rb.linearVelocity.y, 0f);

        rb.angularVelocity = Vector3.zero;

        StartCoroutine(ResetRoutine());
    }

    private IEnumerator ResetRoutine()
    {
        yield return new WaitForSeconds(4f);
        resetPos = false;
    }
}