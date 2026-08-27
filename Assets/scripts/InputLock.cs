using UnityEngine;

/// <summary>
/// Candado global del teclado de juego.
///
/// El panel del nombre del modelo es IMGUI, y un TextField de IMGUI NO bloquea
/// la clase Input antigua, que es la que usan todos los scripts de juego. Sin
/// esto, escribir "Modelo_Rapido" movia al jugador con la 'a' y la 'd',
/// pausaba la grabacion con la 'r', abria las estadisticas con la 'm' y
/// lanzaba un robo con la 'k'.
///
/// Uso: los scripts de juego leen el teclado a traves de esta clase en vez de
/// llamar a Input directamente. Mientras Capturando sea true devuelve valores
/// neutros, asi que las teclas se quedan en el campo de texto.
/// </summary>
public static class InputLock
{
    /// <summary>true mientras la UI esta capturando el teclado.</summary>
    public static bool Capturando { get; private set; }

    /// <summary>Bloquea el teclado de juego. Idempotente.</summary>
    public static void Capturar()
    {
        Capturando = true;
    }

    /// <summary>Devuelve el teclado al juego. Idempotente.</summary>
    public static void Liberar()
    {
        Capturando = false;
    }

    // ── Envoltorios de Input ────────────────────────────────────────────────

    public static bool GetKey(KeyCode tecla)
    {
        return !Capturando && Input.GetKey(tecla);
    }

    public static bool GetKeyDown(KeyCode tecla)
    {
        return !Capturando && Input.GetKeyDown(tecla);
    }

    public static float GetAxisRaw(string eje)
    {
        return Capturando ? 0f : Input.GetAxisRaw(eje);
    }
}
