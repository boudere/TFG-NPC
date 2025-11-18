using UnityEngine;

[DefaultExecutionOrder(10000)] // se ejecuta después de la mayoría de Start()
public class ForceCameraFar : MonoBehaviour
{
    public float far;

    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void Start()
    {
        if (cam != null)
            cam.farClipPlane = far;
    }

    void LateUpdate()
    {
        // Si otro script lo cambia en tiempo de ejecución, lo volvemos a fijar
        if (cam != null && Mathf.Abs(cam.farClipPlane - far) > 0.01f)
            cam.farClipPlane = far;
    }
}
