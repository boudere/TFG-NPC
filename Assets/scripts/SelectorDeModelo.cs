using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Panel para elegir uno de los modelos que el juego ha entrenado.
///
/// Sustituye al explorador de archivos de Windows. Se abre con Abrir() y avisa
/// por callback: onElegido(modelo) o onCancelado(). Quien lo abre decide que
/// hacer despues; este script solo pinta y devuelve la eleccion.
///
/// Es IMGUI a proposito: no necesita que se monte nada en la escena y funciona
/// igual en el Editor y en la build, como los paneles de entrenamiento.
/// </summary>
public class SelectorDeModelo : MonoBehaviour
{
    private bool _abierto;
    private List<ModeloInfo> _modelos = new List<ModeloInfo>();
    private Vector2 _scroll;
    private Action<ModeloInfo> _onElegido;
    private Action _onCancelado;

    // GUI
    private GUIStyle _panelStyle, _filaStyle, _filaHoverStyle;
    private GUIStyle _tituloStyle, _nombreStyle, _detalleStyle, _botonStyle, _vacioStyle;
    private Texture2D _bgPanel, _bgFila, _bgFilaHover, _bgBoton;
    private bool _estilosListos;

    private const int PANEL_W = 620;
    private const int PANEL_H = 460;
    private const int FILA_H = 56;

    public bool Abierto { get { return _abierto; } }

    /// <summary>Abre el panel. Relee la carpeta cada vez, por si acabas de entrenar.</summary>
    public void Abrir(Action<ModeloInfo> onElegido, Action onCancelado)
    {
        _onElegido = onElegido;
        _onCancelado = onCancelado;
        _modelos = ModelosDisponibles.Listar();
        _scroll = Vector2.zero;
        _abierto = true;

        // El teclado pasa a ser de la UI mientras el panel esta abierto.
        InputLock.Capturar();
    }

    public void Cerrar()
    {
        if (!_abierto) return;
        _abierto = false;
        InputLock.Liberar();
    }

    private void OnDisable()
    {
        // Red de seguridad: que el candado no se quede echado si la escena
        // se descarga con el panel abierto.
        if (_abierto) InputLock.Liberar();
        _abierto = false;
    }

    private void OnGUI()
    {
        if (!_abierto) return;

        PrepararEstilos();

        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            Cancelar();
            e.Use();
            return;
        }

        int px = (Screen.width - PANEL_W) / 2;
        int py = (Screen.height - PANEL_H) / 2;

        GUI.Box(new Rect(px, py, PANEL_W, PANEL_H), "", _panelStyle);

        GUI.Label(new Rect(px, py + 14, PANEL_W, 26), "ELIGE UN MODELO", _tituloStyle);

        int listaY = py + 52;
        int listaH = PANEL_H - 52 - 56;

        if (_modelos.Count == 0)
        {
            GUI.Label(new Rect(px + 30, listaY + 30, PANEL_W - 60, 120),
                "Todavia no has entrenado ningun modelo.\n\n" +
                "Ve al modo entrenamiento, graba una partida con la tecla R y " +
                "entrena con la tecla T o al terminar el partido.\n\n" +
                "Los modelos apareceran aqui automaticamente.", _vacioStyle);
        }
        else
        {
            Rect vista = new Rect(px + 14, listaY, PANEL_W - 28, listaH);
            Rect contenido = new Rect(0, 0, PANEL_W - 52, _modelos.Count * (FILA_H + 6));

            _scroll = GUI.BeginScrollView(vista, _scroll, contenido);

            for (int i = 0; i < _modelos.Count; i++)
            {
                ModeloInfo m = _modelos[i];
                Rect fila = new Rect(0, i * (FILA_H + 6), contenido.width, FILA_H);

                bool encima = fila.Contains(Event.current.mousePosition);
                GUI.Box(fila, "", encima ? _filaHoverStyle : _filaStyle);

                GUI.Label(new Rect(fila.x + 14, fila.y + 7, fila.width - 130, 22), m.nombre, _nombreStyle);
                GUI.Label(new Rect(fila.x + 14, fila.y + 29, fila.width - 130, 20), m.Resumen(), _detalleStyle);

                if (GUI.Button(new Rect(fila.xMax - 108, fila.y + 12, 94, 32), "USAR", _botonStyle))
                {
                    Elegir(m);
                    GUI.EndScrollView();
                    return;
                }
            }

            GUI.EndScrollView();
        }

        if (GUI.Button(new Rect(px + 14, py + PANEL_H - 46, PANEL_W - 28, 34), "Cancelar", _botonStyle))
        {
            Cancelar();
            return;
        }
    }

    private void Elegir(ModeloInfo m)
    {
        Action<ModeloInfo> cb = _onElegido;
        Cerrar();
        if (cb != null) cb(m);
    }

    private void Cancelar()
    {
        Action cb = _onCancelado;
        Cerrar();
        if (cb != null) cb();
    }

    private void PrepararEstilos()
    {
        if (_estilosListos) return;

        _bgPanel = Tex(new Color(0.05f, 0.06f, 0.14f, 0.97f));
        _bgFila = Tex(new Color(0.11f, 0.13f, 0.24f, 1f));
        _bgFilaHover = Tex(new Color(0.16f, 0.20f, 0.36f, 1f));
        _bgBoton = Tex(new Color(0.15f, 0.45f, 0.85f, 1f));

        _panelStyle = new GUIStyle(GUI.skin.box); _panelStyle.normal.background = _bgPanel;
        _filaStyle = new GUIStyle(GUI.skin.box); _filaStyle.normal.background = _bgFila;
        _filaHoverStyle = new GUIStyle(GUI.skin.box); _filaHoverStyle.normal.background = _bgFilaHover;

        _tituloStyle = new GUIStyle(GUI.skin.label);
        _tituloStyle.fontSize = 17; _tituloStyle.fontStyle = FontStyle.Bold;
        _tituloStyle.alignment = TextAnchor.MiddleCenter;
        _tituloStyle.normal.textColor = Color.white;

        _nombreStyle = new GUIStyle(GUI.skin.label);
        _nombreStyle.fontSize = 15; _nombreStyle.fontStyle = FontStyle.Bold;
        _nombreStyle.normal.textColor = Color.white;

        _detalleStyle = new GUIStyle(GUI.skin.label);
        _detalleStyle.fontSize = 11;
        _detalleStyle.normal.textColor = new Color(0.72f, 0.80f, 0.94f);

        _vacioStyle = new GUIStyle(GUI.skin.label);
        _vacioStyle.fontSize = 13; _vacioStyle.wordWrap = true;
        _vacioStyle.alignment = TextAnchor.UpperCenter;
        _vacioStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

        _botonStyle = new GUIStyle(GUI.skin.button);
        _botonStyle.fontSize = 13; _botonStyle.fontStyle = FontStyle.Bold;
        _botonStyle.normal.background = _bgBoton;
        _botonStyle.hover.background = _bgBoton;
        _botonStyle.normal.textColor = Color.white;
        _botonStyle.hover.textColor = Color.white;

        _estilosListos = true;
    }

    private static Texture2D Tex(Color c)
    {
        Texture2D t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
