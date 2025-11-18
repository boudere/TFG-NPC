
using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "Character")]
public class Character : ScriptableObject
{
    [Header("Datos del Personaje")]
    public GameObject personajeJugable;
    public Sprite imagen;
    public string nombre;
    public string function;
    public string task;
    public string feature;
    public int id;
    public bool selected;
}