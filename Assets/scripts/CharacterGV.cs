

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
        if (index != characterManager.index)
        {
            return;
        }

         float moveHorizontal = Input.GetAxis("Horizontal"); 
         float moveVertical = Input.GetAxis("Vertical");   

          
            Vector3 movement = new Vector3(moveHorizontal, 0f, moveVertical) * speed;
            rb.linearVelocity = new Vector3(movement.x, rb.linearVelocity.y, movement.z);

      
            if (Input.GetButtonDown("Jump"))
            {
                rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            }
        
    }

}