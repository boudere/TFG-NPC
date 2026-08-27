using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// Fuerza la carga del ONNX Runtime que viene con el juego, antes de que nada
/// mas pueda colar otro.
///
/// EL PROBLEMA QUE RESUELVE
/// Windows 11 trae su propio onnxruntime.dll en System32 (Windows AI). El
/// loader de Windows resuelve las DLL por NOMBRE BASE: si ya hay una
/// "onnxruntime.dll" cargada en el proceso, cualquier DllImport("onnxruntime")
/// posterior se engancha a esa, venga de donde venga.
///
/// El dialogo nativo de abrir archivo (StandaloneFileBrowser) arrastra medio
/// shell de Windows al proceso, y con el la ORT del sistema. A partir de ahi,
/// el envoltorio de C# (compilado contra una ORT mas nueva) pedia
/// OrtGetCompileApi a una version que no la tiene, obtenia un puntero basura y
/// al llamarlo el proceso moria de golpe, sin excepcion que capturar:
///
///   AIControllerFNNClasi:Start -> ReloadModel -> SessionOptions:.ctor
///     -> NativeMethods:.cctor -> CompileApi.NativeMethods:.ctor
///       -> onnxruntime (System32, v1.17) -> violacion de acceso
///
/// LA SOLUCION
/// Cargar la DLL correcta por ruta absoluta nada mas arrancar. Una vez esta en
/// la lista de modulos del proceso, es la que gana.
/// </summary>
public static class OnnxRuntimePreload
{
    /// <summary>
    /// false solo si en Windows no se pudo dejar cargada la DLL correcta.
    /// AIControllerFNNClasi lo consulta para no intentar la inferencia y
    /// cerrar el juego: mejor un NPC quieto y un error en el log.
    /// </summary>
    public static bool Verificado { get; private set; } = true;

    /// <summary>Ruta del onnxruntime.dll que finalmente esta en el proceso.</summary>
    public static string RutaCargada { get; private set; } = "";

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

    private const string NOMBRE_DLL = "onnxruntime.dll";
    private const int LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, int dwFlags);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetModuleFileNameW(IntPtr hModule, StringBuilder lpFilename, int nSize);

    // BeforeSplashScreen es lo mas temprano que hay: mucho antes de que se
    // cargue ninguna escena y, sobre todo, antes de que el usuario pueda abrir
    // el dialogo de archivos.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void Precargar()
    {
        IntPtr yaCargada = GetModuleHandleW(NOMBRE_DLL);
        if (yaCargada != IntPtr.Zero)
        {
            RutaCargada = RutaDe(yaCargada);
            bool esLaNuestra = EsDelJuego(RutaCargada);
            Verificado = esLaNuestra;

            if (esLaNuestra)
                Debug.Log("[OnnxRuntimePreload] Ya estaba cargada la correcta: " + RutaCargada);
            else
                Debug.LogError("[OnnxRuntimePreload] Alguien cargo antes otro " + NOMBRE_DLL +
                               " (" + RutaCargada + "). No se puede sustituir en caliente; " +
                               "la inferencia queda deshabilitada para no cerrar el juego.");
            return;
        }

        string ruta = Path.Combine(Application.dataPath, "Plugins", "x86_64", NOMBRE_DLL);

        if (!File.Exists(ruta))
        {
            Verificado = false;
            Debug.LogError("[OnnxRuntimePreload] No encuentro " + ruta +
                           ". Comprueba que el plugin del paquete com.github.asus4.onnxruntime " +
                           "tiene Win64 activado en su importador.");
            return;
        }

        // LOAD_WITH_ALTERED_SEARCH_PATH para que resuelva sus dependencias
        // (onnxruntime_providers_shared.dll) en su propia carpeta.
        IntPtr h = LoadLibraryExW(ruta, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);

        if (h == IntPtr.Zero)
        {
            Verificado = false;
            Debug.LogError("[OnnxRuntimePreload] LoadLibraryEx fallo con codigo " +
                           Marshal.GetLastWin32Error() + " para " + ruta);
            return;
        }

        RutaCargada = RutaDe(h);
        Verificado = true;
        Debug.Log("[OnnxRuntimePreload] ONNX Runtime precargado desde " + RutaCargada);
    }

    private static bool EsDelJuego(string ruta)
    {
        if (string.IsNullOrEmpty(ruta)) return false;
        return ruta.Replace('\\', '/')
                   .IndexOf(Application.dataPath.Replace('\\', '/'),
                            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string RutaDe(IntPtr modulo)
    {
        StringBuilder sb = new StringBuilder(1024);
        int n = GetModuleFileNameW(modulo, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : "(ruta desconocida)";
    }

#endif
}
