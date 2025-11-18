//using System;
//using Unity.VisualScripting;
//using UnityEngine;



//public class CharacterSelection : MonoBehaviour
//{

//    private Renderer rendererObject;
//    //[SerializeField] private Material defaultMaterial;
//    //[SerializeField] private Material newMaterial;
//    private CharacterManager characterManager = CharacterManager.instance;
//    public static CharacterSelection instance;

//    private PlayerInputActions input;
//    //public int count = 0;
//    //public string selectedTag;
//    public int id;


//    void Start()
//    {

//        if (characterManager == null || characterManager.characterList == null)
//        {
//            Debug.LogError("No hay CharacterManager asignado o la lista está vacía");
//            return;
//        }

//        id = characterManager.index;

//        foreach (Character p in characterManager.characterList)
//        {
//            if (p.id == id && p != null)
//            {
//                CharacterSelection clicked = p.personajeJugable.GetComponent<CharacterSelection>();

//                if (clicked == this)
//                {
//                    rendererObject.material = newMaterial;
//                    Debug.Log($"Material cambiado en {gameObject.name}");
//                }


//            }
//        }


        
//    }
    

//    void Update()
//    {

//        //if (Input.GetMouseButtonDown(0))
//        //{
//        //    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
//        //    RaycastHit hit;

//        //    if (Physics.Raycast(ray, out hit))
//        //    {
//        //        CharacterSelection clicked = hit.collider.GetComponent<CharacterSelection>();

//        //        if (clicked != null && clicked == this)
//        //        {

//        //            ChangeMaterialMethod();

//        //        }

//        //    }

//        //}


//        //characterManager.characterList[id].id;




//    }




//    public void ChangeMaterialMethod(Character p)
//    {
//        // var personajeSeleccionado = characterManager.characterList.Find(c => c.id == id);




//        //if (CharacterManager.LastSelectedCount == 0)
//        //{
//        //    rendererObject.material = newMaterial;
//        //    count = 1;
//        //    CharacterManager.RegisterSelection(tag, count);
//        //}
//        //else
//        //{
//        //    if (CharacterManager.LastSelectedTag == gameObject.tag)
//        //    {
//        //        rendererObject.material = defaultMaterial;
//        //        count = 0;
//        //        CharacterManager.RegisterSelection(tag, count);
//        //    }
//        //}

//        //  p.rendererObject.material = newMaterial;

//        //if (p.id == characterManager.index)
//        //{
//        //    p.material = newMaterial;
//        //}



//    }
//}
