using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public Camera topCamera;
    public AudioListener topAudio;

    public void EnableTopView(bool enabled)
    {
        topCamera.enabled = enabled;
        topAudio.enabled = enabled;
    }



}
