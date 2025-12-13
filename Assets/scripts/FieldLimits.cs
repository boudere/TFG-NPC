using UnityEngine;

public class FieldLimits : MonoBehaviour
{
    public float minX, maxX, minZ, maxZ, y;
    public static FieldLimits instance;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    void Start()
    {
        Renderer rend = GetComponent<Renderer>();
        Bounds b = rend.bounds;

        minX = b.min.x;
        maxX = b.max.x;
        minZ = b.min.z;
        maxZ = b.max.z;
        y = b.center.y;
    }
}
