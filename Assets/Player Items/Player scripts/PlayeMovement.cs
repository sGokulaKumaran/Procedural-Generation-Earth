using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8f;
    public float crouchSpeed = 2f;
    public float gravity = -9.81f;

    [Header("Crouch")]
    public Transform playerCamera;
    public float crouchHeight = 1f;
    private float originalHeight;
    private Vector3 originalCamPos;
    private bool isCrouching;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundDistance = 0.4f;
    public LayerMask groundMask;

    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;


    

    public bool IsMoving { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsCrouching => isCrouching;


    void Start()
    {
        controller = GetComponent<CharacterController>();
        originalHeight = controller.height;
        originalCamPos = playerCamera.localPosition;
       
    }
    

    void Update()
    {
        // Ground Check
        isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
        if (isGrounded && velocity.y < 0)
            velocity.y = -2f;

        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        Vector3 move = transform.right * x + transform.forward * z;
        IsMoving = move.magnitude > 0.1f;

        // Sprint
        IsSprinting = Input.GetKey(KeyCode.LeftShift) && !isCrouching;
        float speed = IsSprinting ? sprintSpeed : (isCrouching ? crouchSpeed : walkSpeed);

        // Apply movement
        controller.Move(move * speed * Time.deltaTime);

        // Apply gravity
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // Crouch (Sit)
        if (Input.GetKeyDown(KeyCode.LeftControl))
        {
            isCrouching = true;
            controller.height = crouchHeight;
            playerCamera.localPosition = originalCamPos + Vector3.down * 0.5f;
        }
        else if (Input.GetKeyUp(KeyCode.LeftControl))
        {
            isCrouching = false;
            controller.height = originalHeight;
            playerCamera.localPosition = originalCamPos;
        }

    }
 
}
