using UnityEngine;

public class CharacterManagerInField : MonoBehaviour


{
    public static CharacterManager characterManager;
   [SerializeField] Camera camY;
    private DebugCharacterCamera debugCam;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
            characterManager = CharacterManager.instance;
        cameraConfiguration(characterManager.index);
        }

    public void cameraConfiguration(int i)
    {

        //camY = GameObject.FindGameObjectWithTag("MainCamera").GetComponent<Camera>();

        var camGO = GameObject.FindGameObjectWithTag("MainCamera");
        debugCam = camGO.GetComponent<DebugCharacterCamera>();

        Debug.Log("camGO encontrado = " + camGO.name);
        Debug.Log("camGO escena = " + camGO.scene.name);

        //switch (i)
        //{
        //    case 0:

        //        camY.transform.position = new Vector3(-261, 300, 265);
        //        if (camY != null)
        //            Debug.Log("Pos cam en Update: " + camY.transform.position);
        //        camY.gameObject.SetActive(false);

        //        break;

        //    case 1: break;
        //    case 2: break;
        //    case 3: break;
        //    case 4: break;
        //    case 5: break;
        //    default: break;
        //}

        debugCam.SetConfig(i);



    }
}
