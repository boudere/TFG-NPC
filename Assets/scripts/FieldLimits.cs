
using UnityEngine;

public class FieldLimits : MonoBehaviour
{
    [Header("Field Limits")]
    public float minX;
    public float maxX;
    public float minZ;
    public float maxZ;
    public float y;

    public static FieldLimits instance;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
}