using UnityEngine;

public class DebugCharacterCamera : MonoBehaviour
{
    
    public void SetConfig(int i)
    {
        switch (i)
        {
            case 0:
               
                    transform.position = new Vector3(-261, 300, 265);
                   
                
                break;
            case 1: 
                transform.position = new Vector3(-240, 300, -115); 
                break;
            case 2:
                transform.position = new Vector3(-8, 300, 252); 
                break;
            case 3: 
                transform.position = new Vector3(47, 300, -124); 
                break;
            case 4: 
                transform.position = new Vector3(293, 300, -153); 
                break;
            case 5:
                transform.position = new Vector3(266, 300, 239);
                break;


        }
    }
}
