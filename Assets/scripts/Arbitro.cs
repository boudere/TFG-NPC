using UnityEngine;

public class Arbitro : MonoBehaviour
{
    public static Arbitro instance;
    private PlayerID pID;
    int teamBola = 0; // Estados de la bola 0: libre, 1: team1, 2: team2

    private void Awake()
    {
        instance = this;
    }

    public void BallEntraEnArea(PlayerID player)
    {
        Debug.Log($"La pelota ENTRÓ en el área del jugador ID: {player.id}");
        pID = player;
        quienTieneLaBola(player);

    }

    public void BallSaleDeArea(PlayerID player)
    {
        Debug.Log($"La pelota SALIÓ del área del jugador ID: {player.id}");
        pID = player;
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
}
