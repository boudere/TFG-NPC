using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Metadatos que TrainingUploader guarda junto a cada modelo entrenado.
/// Se serializa como meta_&lt;nombre&gt;.json en persistentDataPath.
/// </summary>
[Serializable]
public class ModeloMeta
{
    public string nombre = "";
    public string fechaISO = "";
    public int frames = 0;
    public float accMov = -1f;
    public float f1Mov = -1f;
    public float accShoot = -1f;
    public float accPass = -1f;
    public float lossFinal = -1f;

    public bool TieneMetricas { get { return accMov >= 0f; } }
}

/// <summary>Un modelo listo para usar: onnx + scaler emparejados.</summary>
public class ModeloInfo
{
    public string nombre;
    public string rutaOnnx;
    public string rutaScaler;
    public DateTime fecha;
    public ModeloMeta meta;   // null si el modelo se entreno antes de que se guardaran metadatos

    public string Resumen()
    {
        string s = fecha.ToString("dd/MM/yyyy HH:mm");
        if (meta != null && meta.TieneMetricas)
        {
            s += "   Mov " + meta.accMov.ToString("F0") + "%" +
                 "   Disparo " + meta.accShoot.ToString("F0") + "%" +
                 "   Pase " + meta.accPass.ToString("F0") + "%";
            if (meta.frames > 0) s += "   " + meta.frames + " frames";
        }
        else
        {
            s += "   (sin metricas: entrenado antes de que se guardaran)";
        }
        return s;
    }
}

/// <summary>
/// Enumera los modelos que el propio juego ha entrenado.
///
/// Sustituye al explorador de archivos de Windows, que tenia tres problemas:
/// arrastraba el shell al proceso (y con el un onnxruntime.dll incompatible que
/// cerraba el juego), dejaba elegir modelos de otra arquitectura, y permitia
/// emparejar un modelo con el scaler equivocado, que no falla pero decide mal.
///
/// La convencion de nombres la crea TrainingUploader:
///     SoccerModel_&lt;nombre&gt;.onnx  +  scaler_&lt;nombre&gt;.json  [+ meta_&lt;nombre&gt;.json]
/// </summary>
public static class ModelosDisponibles
{
    public const string PREFIJO_MODELO = "SoccerModel_";
    public const string PREFIJO_SCALER = "scaler_";
    public const string PREFIJO_META = "meta_";

    /// <summary>Copia de trabajo que mantiene TrainingUploader; no es un modelo con nombre propio.</summary>
    public const string NOMBRE_ACTIVO = "Active";

    public static string Carpeta { get { return Application.persistentDataPath; } }

    public static string RutaOnnx(string nombre)
    {
        return Path.Combine(Carpeta, PREFIJO_MODELO + nombre + ".onnx");
    }

    public static string RutaScaler(string nombre)
    {
        return Path.Combine(Carpeta, PREFIJO_SCALER + nombre + ".json");
    }

    public static string RutaMeta(string nombre)
    {
        return Path.Combine(Carpeta, PREFIJO_META + nombre + ".json");
    }

    /// <summary>
    /// Modelos utilizables, del mas reciente al mas antiguo.
    /// Solo entra un modelo si SU scaler existe: emparejar mal el scaler no da
    /// ningun error, solo un NPC que se mueve y decide mal, que es el fallo mas
    /// dificil de detectar. Aqui se hace imposible.
    /// </summary>
    public static List<ModeloInfo> Listar()
    {
        List<ModeloInfo> lista = new List<ModeloInfo>();

        string carpeta = Carpeta;
        if (!Directory.Exists(carpeta)) return lista;

        string[] ficheros;
        try
        {
            ficheros = Directory.GetFiles(carpeta, PREFIJO_MODELO + "*.onnx");
        }
        catch (Exception e)
        {
            Debug.LogError("[ModelosDisponibles] No se pudo leer " + carpeta + ": " + e.Message);
            return lista;
        }

        foreach (string ruta in ficheros)
        {
            string fichero = Path.GetFileNameWithoutExtension(ruta);
            if (!fichero.StartsWith(PREFIJO_MODELO, StringComparison.Ordinal)) continue;

            string nombre = fichero.Substring(PREFIJO_MODELO.Length);
            if (string.IsNullOrEmpty(nombre)) continue;

            // "Active" es la copia del ultimo entrenado, no un nombre del usuario.
            if (string.Equals(nombre, NOMBRE_ACTIVO, StringComparison.OrdinalIgnoreCase)) continue;

            string scaler = RutaScaler(nombre);
            if (!File.Exists(scaler))
            {
                Debug.LogWarning("[ModelosDisponibles] '" + nombre + "' se ignora: falta su " +
                                 Path.GetFileName(scaler) + ".");
                continue;
            }

            ModeloInfo info = new ModeloInfo();
            info.nombre = nombre;
            info.rutaOnnx = ruta;
            info.rutaScaler = scaler;
            info.meta = LeerMeta(nombre);

            try { info.fecha = File.GetLastWriteTime(ruta); }
            catch { info.fecha = DateTime.MinValue; }

            lista.Add(info);
        }

        lista.Sort((a, b) => b.fecha.CompareTo(a.fecha));
        return lista;
    }

    private static ModeloMeta LeerMeta(string nombre)
    {
        string ruta = RutaMeta(nombre);
        if (!File.Exists(ruta)) return null;

        try
        {
            return JsonUtility.FromJson<ModeloMeta>(File.ReadAllText(ruta));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ModelosDisponibles] meta de '" + nombre + "' ilegible: " + e.Message);
            return null;
        }
    }

    /// <summary>Guarda los metadatos de un modelo recien entrenado.</summary>
    public static void GuardarMeta(ModeloMeta meta)
    {
        if (meta == null || string.IsNullOrEmpty(meta.nombre)) return;

        try
        {
            File.WriteAllText(RutaMeta(meta.nombre), JsonUtility.ToJson(meta, true));
        }
        catch (Exception e)
        {
            // No es critico: sin meta el modelo sigue siendo utilizable,
            // solo aparece en la lista sin metricas.
            Debug.LogWarning("[ModelosDisponibles] No se pudo guardar la meta de '" +
                             meta.nombre + "': " + e.Message);
        }
    }
}
