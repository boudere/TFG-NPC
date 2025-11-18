using UnityEngine;
using StarterAssets;   // el paquete del FirstPersonController

public class EnableFP : MonoBehaviour
{
    private CharacterManager characterManager;
    public int index;                  // índice de este jugador en tu characterList
    public bool selected;

    private FirstPersonController fpsController;
    private Camera fpsCamera;
    private AudioListener audioListener;

    private void Awake()
    {
        characterManager = CharacterManager.instance;

        // Controller en este mismo objeto
        fpsController = GetComponent<FirstPersonController>();

        // Cámara FPS como hijo (la que hemos creado)
        fpsCamera = GetComponentInChildren<Camera>(true);
        if (fpsCamera != null)
            audioListener = fpsCamera.GetComponent<AudioListener>();
    }

    private void Update()
    {
        
        if (characterManager == null) return;
        if (index < 0 || index >= characterManager.characterList.Count) return;

        bool selected = characterManager.characterList[index].selected;

        // Solo el jugador seleccionado tiene control + cámara + audio
        if (fpsController != null) fpsController.enabled = selected;
        if (fpsCamera != null) fpsCamera.enabled = selected;
        if (audioListener != null) audioListener.enabled = selected;
    }
}
