
using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "Character")]
public class Character : ScriptableObject
{
    public GameObject personajeJugable;
    public Sprite imagen;
    public Sprite selectImagen;
    public Sprite label;
    public string nombre;
    public string function;
    public string task;
    public string feature;
    public int id;
    public bool selected;
    public Vector3 position;
}