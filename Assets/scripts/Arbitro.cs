using UnityEngine;

public class Arbitro : MonoBehaviour
{
    public static Arbitro instance;
    public PlayerID currentBallOwner;
   

    int teamBola = 0; // Estados de la bola 0: libre, 1: team1, 2: team2

    private void Awake()
    {
        instance = this;
    }

    public void BallEntraEnArea(PlayerID player)
    {
        Debug.Log($"La pelota ENTRÓ en el área del jugador ID: {player.id}");
        currentBallOwner = player;
        quienTieneLaBola(player);

    }

    public void BallSaleDeArea(PlayerID player)
    {
        Debug.Log($"La pelota SALIÓ del área del jugador ID: {player.id}");
        currentBallOwner = player;
        quienTieneLaBola(player);
    }

    public void quienTieneLaBola(PlayerID player)
    {
        if (player.id % 2 == 0)
        {
            teamBola = 1;
            Debug.Log($"La bola la tiene el equipo 1, el jugador {player}");

        } else if (player.id % 2 == 1)
        {
            teamBola = 2;
            Debug.Log($"La bola la tiene el equipo 2, el jugador {player}");
        }
    }

    public void AsignarBola(PlayerID newOwner, Collider ball)
    {
        currentBallOwner = newOwner;

        Rigidbody rb = ball.GetComponent<Rigidbody>();
     
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;

        // Ignorar colisión con el dueño
        Collider ownerCollider = newOwner.GetComponent<Collider>();
        Physics.IgnoreCollision(ball, ownerCollider, true);

    }

    public void robarBola(PlayerID player)
    {

    }
}
