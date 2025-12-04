

using UnityEngine;

public class CharacterGV : MonoBehaviour
{
    public float speed = 5f;
    public float jumpForce = 7f;
    private Rigidbody rb;
    public int index;
    private CharacterManager characterManager;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        characterManager = CharacterManager.instance;
    }


    private void Update()
    {


        //if (characterManager.index != index)
        //    return;



        if (index != characterManager.index) {
            Debug.Log(index);
            return;
        } 
       



            float moveHorizontal = Input.GetAxis("Horizontal");   // A / D o flechas
            float moveVertical = Input.GetAxis("Vertical");     // W / S o flechas

            // Dirección de movimiento en el plano XZ
            Vector3 movement = new Vector3(moveHorizontal, 0f, moveVertical) * speed;

            // Aplicamos velocidad manteniendo la Y del rigidbody (gravedad, salto, etc.)
            rb.linearVelocity = new Vector3(movement.x, rb.linearVelocity.y, movement.z);

            // Salto
            if (Input.GetButtonDown("Jump"))
            {
                rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            }
        
    }

}