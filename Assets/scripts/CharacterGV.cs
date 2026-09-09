using System.Collections;
using UnityEngine;

public class CharacterGV : PlayerID
{
    [SerializeField] private float turnSpeedDeg = 540f;
    [SerializeField] private float directionLerp = 12f;
    [SerializeField] private float sprintSpeed = 300f;
    [SerializeField] private float sprintMaxDistance = 400f;

    [Header("Sprint - ajustes")]
    [Tooltip("Radio para dar por llegado el sprint. En ejecucion se amplia solo si el jugador avanza mas que este radio en un paso de fisica.")]
    [SerializeField] private float sprintArrivalRadius = 15f;
    [Tooltip("Segundos maximos que puede durar un sprint. Red de seguridad para que el flag nunca se quede encendido.")]
    [SerializeField] private float sprintTimeout = 4f;
    [Tooltip("Si el objetivo es un jugador o el balon, recalcula la direccion cada frame en vez de correr en linea recta a ciegas.")]
    [SerializeField] private bool sprintPersigueObjetivo = true;
    [Tooltip("Si al perseguir nos alejamos del objetivo mas que esto respecto a lo mas cerca que llegamos, se aborta: el objetivo corre mas que nosotros y seguir es malgastar carrera.")]
    [SerializeField] private float sprintToleranciaPerdida = 40f;
    [Tooltip("Margen de carrera sobre la distancia inicial al objetivo.")]
    [SerializeField] private float sprintMargenPersecucion = 1.25f;
    [Tooltip("Logs en consola para diagnosticar el sprint.")]
    [SerializeField] private bool sprintDebugLogs = false;

    private Vector3 smoothDir = Vector3.forward;

    public int index;
    private CharacterManager characterManager;

    private bool resetPos;

    // Flag de estado interno. Antes era publico y se podia marcar a mano en el
    // inspector, lo que dejaba al jugador inmovil y sin control por teclado.
    private bool sprint;
    public bool IsSprinting { get { return sprint; } }

    private Vector3 sprintDirection;
    private Vector3 sprintInitPosition;
    private Vector3 sprintTargetPosition;
    private Transform sprintTargetTransform;
    private float sprintEndTime;
    private float sprintAllowedDistance;
    private float sprintMejorDistancia;
    private bool sprintObjetivoEsBalonSuelto;

    public bool defaultMove = true;
    public static CharacterGV instance;

    public bool debeRobarAlLlegar = false;

    private void Start()
    {
        instance = this;
        characterManager = CharacterManager.instance;

        // Cada jugador arrastra tres numeros que tienen que valer lo mismo:
        // el id del componente de rol (Defensa/CentroCampista/Delantero),
        // el id de este CharacterGV y este index. Si no coinciden, MovePlayer
        // sale antes de mover y el jugador no responde ni al teclado ni a la K.
        if (index != id)
        {
            Debug.LogWarning(
                "[CharacterGV] '" + name + "': index=" + index + " pero id=" + id +
                ". Deben coincidir o este jugador no respondera al control ni al sprint.",
                this);
        }
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
        if (characterManager == null)
        {
            characterManager = CharacterManager.instance;
            if (characterManager == null) return;
        }

        if (stop)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        if (index != characterManager.index)
        {
            // Si dejamos de ser el jugador controlado en mitad de un sprint hay
            // que apagar el flag: si no, se queda encendido para siempre y este
            // jugador ya no vuelve a responder al teclado.
            if (sprint) StopSprint(false);
            return;
        }

        Vector3 moveDir;

        if (sprint)
        {
            if (!ActualizarSprint(out moveDir))
                return;
        }
        else
        {
            // InputLock: mientras se escribe el nombre del modelo el
            // teclado pertenece a la UI, no al jugador.
            float h = InputLock.GetAxisRaw("Horizontal");
            float v = InputLock.GetAxisRaw("Vertical");

            moveDir = new Vector3(h, 0f, v).normalized;
        }

        if (index >= 0 && index < 4)
        {
            npcSpeed = 120;
        }
        float speed = sprint ? sprintSpeed : npcSpeed;


       
        if (Bola.instance.transform.IsChildOf(this.transform))
        {
            speed *= 0.85f;
        }


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

    /// <summary>
    /// Avanza un frame de sprint. Devuelve false si el sprint ha terminado;
    /// en ese caso ya se ha llamado a StopSprint y no hay que mover nada.
    /// </summary>
    private bool ActualizarSprint(out Vector3 moveDir)
    {
        moveDir = sprintDirection;

        // 1. Red de seguridad temporal: pase lo que pase, el sprint acaba.
        if (Time.time >= sprintEndTime)
        {
            if (sprintDebugLogs) Debug.Log("[Sprint] fin por timeout");
            StopSprint(true);
            return false;
        }

        // 2. Si perseguiamos un balon suelto y alguien ya lo ha cogido, se acabo
        //    el motivo del sprint. Sin esto seguiamos corriendo detras de un balon
        //    que ya no era alcanzable y nos pasabamos de largo.
        if (sprintObjetivoEsBalonSuelto && (Bola.instance == null || Bola.instance.EnPosesion))
        {
            if (sprintDebugLogs) Debug.Log("[Sprint] el balon ya tiene duenyo, aborto");
            StopSprint(false);
            return false;
        }

        // 3. Objetivo vivo: si perseguimos, su posicion cambia cada frame.
        if (sprintPersigueObjetivo && sprintTargetTransform != null)
            sprintTargetPosition = sprintTargetTransform.position;

        // 4. Tope de distancia recorrida.
        if (Vector3.Distance(transform.position, sprintInitPosition) >= sprintAllowedDistance)
        {
            if (sprintDebugLogs) Debug.Log("[Sprint] fin por distancia maxima recorrida");
            StopSprint(true);
            return false;
        }

        Vector3 haciaObjetivo = sprintTargetPosition - transform.position;
        haciaObjetivo.y = 0f;
        float distanciaAlObjetivo = haciaObjetivo.magnitude;

        // 5. Si nos estamos alejando del objetivo en vez de acercarnos, es que
        //    corre mas que nosotros (tipico con el balon recien lanzado a 300 u/s).
        //    Seguir solo sirve para pasarse de largo.
        if (distanciaAlObjetivo < sprintMejorDistancia)
        {
            sprintMejorDistancia = distanciaAlObjetivo;
        }
        else if (distanciaAlObjetivo > sprintMejorDistancia + sprintToleranciaPerdida)
        {
            if (sprintDebugLogs)
                Debug.Log("[Sprint] me alejo del objetivo (" + distanciaAlObjetivo.ToString("F0") +
                          " vs mejor " + sprintMejorDistancia.ToString("F0") + "), aborto");
            StopSprint(true);
            return false;
        }

        // 6. Radio de llegada proporcional al avance por paso de fisica.
        //    A 300 u/s el rigidbody avanza ~6 unidades por FixedUpdate, asi que
        //    un radio fijo de 5 se atravesaba de un salto y no se detectaba nunca.
        float avancePorPaso = sprintSpeed * Time.fixedDeltaTime;
        float radioLlegada = Mathf.Max(sprintArrivalRadius, avancePorPaso * 1.5f);

        bool haLlegado = distanciaAlObjetivo <= radioLlegada;

        // 7. Si corremos a ciegas hacia un punto fijo, mirar tambien si lo hemos rebasado.
        if (!haLlegado && !sprintPersigueObjetivo)
            haLlegado = Vector3.Dot(haciaObjetivo, sprintDirection) <= 0f;

        if (haLlegado)
        {
            if (sprintDebugLogs)
                Debug.Log("[Sprint] llegada a " + distanciaAlObjetivo.ToString("F1") +
                          " (radio " + radioLlegada.ToString("F1") + ")");
            StopSprint(true);
            return false;
        }

        // 8. Recalcular direccion si perseguimos un objetivo movil.
        if (sprintPersigueObjetivo && distanciaAlObjetivo > 0.01f)
            sprintDirection = haciaObjetivo / distanciaAlObjetivo;

        moveDir = sprintDirection;
        return true;
    }

    public void SetSprint(Vector3 targetPosition)
    {
        IniciarSprint(targetPosition, null);
    }

    public void SetSprintYRobar(Vector3 targetPosition, PlayerID playerID)
    {
        IniciarSprint(targetPosition, playerID != null ? playerID.transform : null);
        debeRobarAlLlegar = sprint;
    }

    /// <summary>
    /// Sprint hacia un objetivo movil: el rival con balon, o el balon suelto.
    /// robarAlLlegar debe ser false si no hay poseedor, porque un "robo" sin
    /// duenyo lanzaria el balon hacia el jugador de forma antinatural.
    /// </summary>
    public void SetSprintHacia(Transform objetivo, bool robarAlLlegar)
    {
        if (objetivo == null) return;
        IniciarSprint(objetivo.position, objetivo);
        debeRobarAlLlegar = sprint && robarAlLlegar;
        sprintObjetivoEsBalonSuelto = sprint && !robarAlLlegar;
    }

    private void IniciarSprint(Vector3 targetPosition, Transform targetTransform)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
            return;

        float distanciaInicial = direction.magnitude;

        sprintInitPosition = transform.position;
        sprintTargetPosition = targetPosition;
        sprintTargetTransform = targetTransform;
        sprintDirection = direction / distanciaInicial;
        sprintEndTime = Time.time + sprintTimeout;

        // La carrera permitida se deriva de lo lejos que esta el objetivo, no de
        // un numero fijo: con un tope fijo de 400 se corrian 400 aunque el balon
        // estuviera a 150. sprintMaxDistance queda como techo de seguridad, pero
        // nunca por debajo de lo que hace falta para llegar al objetivo.
        float necesaria = distanciaInicial * sprintMargenPersecucion + 25f;
        float techo = Mathf.Max(sprintMaxDistance, distanciaInicial + 25f);
        sprintAllowedDistance = Mathf.Min(necesaria, techo);
        sprintMejorDistancia = distanciaInicial;

        sprint = true;
        debeRobarAlLlegar = false;
        sprintObjetivoEsBalonSuelto = false;

        if (sprintDebugLogs)
            Debug.Log("[Sprint] inicio, objetivo a " + distanciaInicial.ToString("F0") +
                      ", tope " + sprintAllowedDistance.ToString("F0"));
    }

    private void StopSprint(bool intentarRoboSiProcede)
    {
        bool intentarRobo = intentarRoboSiProcede && debeRobarAlLlegar;

        sprint = false;
        sprintDirection = Vector3.zero;
        sprintTargetTransform = null;
        debeRobarAlLlegar = false;
        sprintObjetivoEsBalonSuelto = false;

        if (rb != null)
        {
            rb.linearVelocity = new Vector3(
                0f,
                rb.linearVelocity.y,
                0f
            );
        }

        if (!intentarRobo) return;
        if (WinTheBall.instance == null || Bola.instance == null) return;

        // Al llegar corriendo estamos, como mucho, a radioLlegada del rival, asi
        // que el umbral no puede ser menor que eso o el sprint nunca consumaria
        // el robo por muy bajo que se ponga distanciaRoboK.
        float umbral = Mathf.Max(WinTheBall.instance.distanciaRoboK, sprintArrivalRadius + 5f);
        float distBalon = Vector3.Distance(transform.position, Bola.instance.transform.position);
        if (distBalon <= umbral)
        {
            WinTheBall.instance.EjecutarRobo(gameObject);
        }
        else if (sprintDebugLogs)
        {
            Debug.Log("[Sprint] llegue, pero el balon esta a " + distBalon.ToString("F0") +
                      " (umbral " + umbral.ToString("F0") + "): no robo");
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

        yield return new WaitForSeconds(0.5f);

        stop = false;
        playerStop = null;
    }

    public override bool EnReset { get { return resetPos; } }

    public void ResetToSpawn()
    {
        resetPos = true;

        if (sprint) StopSprint(false);

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
