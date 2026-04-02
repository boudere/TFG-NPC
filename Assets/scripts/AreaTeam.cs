using System.Collections.Generic;
using UnityEngine;

public class AreaTeam : MonoBehaviour
{
    public List<Collider> franjas = new List<Collider>();

    private void Awake()
    {
        // Auto-detectar todas las franjas hijas
        franjas.Clear();
        foreach (Transform child in transform)
        {
            Collider col = child.GetComponent<Collider>();
            if (col != null && col.isTrigger)
            {
                franjas.Add(col);
            }
        }
    }

    // ? Saber si un punto está dentro del área total
    public bool IsInsideArea(Vector3 point)
    {
        foreach (Collider col in franjas)
        {
            if (col.bounds.Contains(point))
                return true;
        }
        return false;
    }

    // ?? Obtener punto aleatorio dentro del área total
    public Vector3 GetRandomPoint()
    {
        if (franjas.Count == 0)
            return transform.position;

        // Elegir una franja aleatoria
        Collider col = franjas[Random.Range(0, franjas.Count)];

        Bounds b = col.bounds;

        float x = Random.Range(b.min.x, b.max.x);
        float y = Random.Range(b.min.y, b.max.y);
        float z = Random.Range(b.min.z, b.max.z);

        return new Vector3(x, y, z);
    }
}