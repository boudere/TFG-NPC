using System.Collections.Generic;
using UnityEngine;

public class WinTheBall : MonoBehaviour
{
    private GameObject[] p;
    public static CharacterManager characterManager;
    public static TriggerHavePlayer havePlayer;
    public static Bola ball;
    public static WinTheBall instance;

    [Tooltip("Distancia a la que se puede robar directamente, sin sprint.")]
    public float distanciaMaxima;

    [Header("Sprint con K")]
    [Tooltip("Distancia a la que se consuma el robo con K, tanto al pulsarla estando cerca como al terminar el sprint. Cuanto menor, mas pegado hay que estar. La tecla L sigue usando distanciaMaxima.")]
    public float distanciaRoboK = 35f;
    [Tooltip("Distancia maxima a la que la K inicia un sprint. Por debajo de distanciaRoboK el robo es directo.")]
    public float rangoSprint = 600f;
    [Tooltip("Si el balon esta suelto, la K corre hacia el balon en vez de no hacer nada.")]
    public bool perseguirBalonSuelto = true;
    [Tooltip("Logs en consola para diagnosticar por que la K no hace nada.")]
    public bool debugLogs = false;

    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        p = GetAllFieldPlayers();
        characterManager = CharacterManager.instance;
        ball = Bola.instance;
        havePlayer = TriggerHavePlayer.instance;
    }

    void Update()
    {
        // La escena de jugadores se carga en aditivo, asi que en el Start de
        // esta escena puede que todavia no hubiera ningun jugador que encontrar.
        if (p == null || p.Length == 0)
            p = GetAllFieldPlayers();

        if (characterManager == null) characterManager = CharacterManager.instance;
        if (ball == null) ball = Bola.instance;

        if (characterManager == null || Bola.instance == null || p.Length == 0)
            return;

        if (InputLock.GetKeyDown(KeyCode.L))
            RoboDirecto();

        // if separado, no else if: antes, pulsar L y K en el mismo frame
        // hacia que la K se descartara.
        if (InputLock.GetKeyDown(KeyCode.K))
            RoboConSprint();
    }

    /// <summary>
    /// Devuelve el CharacterGV del jugador que controla el usuario.
    /// Se busca por CharacterGV y no por GetComponent&lt;PlayerID&gt;() porque cada
    /// jugador lleva DOS componentes que heredan de PlayerID (CharacterGV y el
    /// rol: Defensa/CentroCampista/Delantero), cada uno con su propio id, y
    /// GetComponent devolvia uno u otro segun el orden de componentes.
    /// CharacterGV es ademas el componente que ejecuta el movimiento, asi que
    /// asi nos aseguramos de que el que mandamos correr es el que corre.
    /// </summary>
    private CharacterGV GetJugadorControlado()
    {
        for (int i = 0; i < p.Length; i++)
        {
            if (p[i] == null) continue;
            CharacterGV c = p[i].GetComponent<CharacterGV>();
            if (c != null && c.index == characterManager.index)
                return c;
        }
        return null;
    }

    private void RoboDirecto()
    {
        CharacterGV yo = GetJugadorControlado();
        if (yo == null)
        {
            if (debugLogs) Debug.Log("[L] no encuentro al jugador controlado (index=" + characterManager.index + ")");
            return;
        }

        if (Bola.instance.transform.IsChildOf(yo.transform)) return;

        if (Bola.instance.EnPosesion && !EsPoseedorValidoParaRobar(Bola.instance.Owner))
            return;

        float distancia = Vector3.Distance(yo.transform.position, Bola.instance.transform.position);
        if (distancia < distanciaMaxima)
            EjecutarRobo(yo.gameObject);
        else if (debugLogs)
            Debug.Log("[L] balon a " + distancia.ToString("F0") + ", fuera de distanciaMaxima (" + distanciaMaxima + ")");
    }

    private void RoboConSprint()
    {
        CharacterGV yo = GetJugadorControlado();
        if (yo == null)
        {
            if (debugLogs) Debug.Log("[K] no encuentro al jugador controlado (index=" + characterManager.index + ")");
            return;
        }

        if (Bola.instance.transform.IsChildOf(yo.transform))
        {
            if (debugLogs) Debug.Log("[K] ya llevo yo el balon");
            return;
        }

        Transform objetivo;
        bool hayPoseedor;

        if (Bola.instance.EnPosesion && Bola.instance.Owner != null)
        {
            if (!EsPoseedorValidoParaRobar(Bola.instance.Owner))
            {
                if (debugLogs) Debug.Log("[K] el poseedor no es robable (companero o portero)");
                return;
            }
            objetivo = Bola.instance.Owner.transform;
            hayPoseedor = true;
        }
        else
        {
            // Balon suelto: no hay poseedor al que robar. Antes esto era un
            // return mudo, que es una de las razones de que la K "a veces" no
            // hiciera nada: bastaba pulsarla con el balon en el aire.
            if (!perseguirBalonSuelto)
            {
                if (debugLogs) Debug.Log("[K] balon suelto y perseguirBalonSuelto esta desactivado");
                return;
            }
            objetivo = Bola.instance.transform;
            hayPoseedor = false;
        }

        float distancia = Vector3.Distance(yo.transform.position, objetivo.position);

        if (distancia <= distanciaRoboK)
        {
            // Solo tiene sentido "robar" si hay a quien: con el balon suelto
            // y ya al lado, deja que lo recoja el trigger normal.
            if (hayPoseedor) EjecutarRobo(yo.gameObject);
            else if (debugLogs) Debug.Log("[K] balon suelto y ya estoy encima, no hago nada");
            return;
        }

        if (distancia > rangoSprint)
        {
            if (debugLogs)
                Debug.Log("[K] objetivo a " + distancia.ToString("F0") + ", fuera del rango de sprint (" + rangoSprint + ")");
            return;
        }

        yo.SetSprintHacia(objetivo, hayPoseedor);
    }

    /// <summary>
    /// Version para el jugador humano: el ladron es el que controla el usuario.
    /// </summary>
    public bool EsPoseedorValidoParaRobar(PlayerID owner)
    {
        if (characterManager == null) characterManager = CharacterManager.instance;
        return EsPoseedorValidoParaRobar(owner, characterManager != null ? characterManager.index : -1);
    }

    /// <summary>
    /// Decide si 'owner' es un objetivo legitimo para QUIEN quiere robar.
    ///
    /// Antes solo existia la version de arriba, que comparaba siempre contra
    /// characterManager.index, o sea contra el equipo del HUMANO. Para tu tecla
    /// L era correcto, pero cuando quien robaba era un NPC la pregunta estaba
    /// mal referenciada: "es del equipo del humano?" en vez de "es del equipo
    /// del que roba?". Si el NPC jugaba en el equipo contrario al tuyo, la
    /// condicion quedaba invertida y el robo estaba bloqueado justo cuando
    /// tenia sentido. Por eso el modelo no ejecutaba nunca la K ni la L.
    /// </summary>
    public bool EsPoseedorValidoParaRobar(PlayerID owner, int idLadron)
    {
        if (owner == null) return true;
        if (owner.CompareTag("Portero") || owner is Portero) return false;
        if (idLadron < 0) return true;
        if (owner.id == idLadron) return false;            // no se roba a si mismo
        if (owner.id % 2 == idLadron % 2) return false;    // mismo equipo
        return true;
    }

    public void EjecutarRobo(GameObject playerGo)
    {
        if (characterManager == null) characterManager = CharacterManager.instance;
        EjecutarRobo(playerGo, characterManager != null ? characterManager.index : -1);
    }

    public void EjecutarRobo(GameObject playerGo, int idLadron)
    {
        if (playerGo == null) return;
        if (Bola.instance == null) return;
        if (Bola.instance.EnPosesion && !EsPoseedorValidoParaRobar(Bola.instance.Owner, idLadron))
        {
            return;
        }

        if (havePlayer == null) havePlayer = TriggerHavePlayer.instance;
        if (havePlayer != null) havePlayer.setRobo(true);

        Bola.instance.Soltar();
        Rigidbody rb = Bola.instance.GetComponent<Rigidbody>();
        rb.isKinematic = false;
        Vector3 direction = (playerGo.transform.position - Bola.instance.transform.position).normalized;
        float passSpeed = 300f;
        rb.linearVelocity = direction * passSpeed;
        rb.angularVelocity = Vector3.zero;
    }

    private GameObject[] GetAllFieldPlayers()
    {
        List<GameObject> allPlayers = new List<GameObject>();

        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("Defensa"));
        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("CentroCampista"));
        allPlayers.AddRange(GameObject.FindGameObjectsWithTag("Delantero"));

        return allPlayers.ToArray();
    }
}
