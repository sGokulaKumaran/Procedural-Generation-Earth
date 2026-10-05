using UnityEngine;

/// <summary>
/// Attach to Main Camera. 
/// WASD to move, QE for up/down, Shift to sprint, Right Mouse to look around.
/// </summary>
[RequireComponent(typeof(Camera))]
public class SmoothCamera : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float moveSpeed = 20f;
    public float sprintMultiplier = 3f;
    public float lookSensitivity = 2f;

    [Header("Smoothing")]
    [Tooltip("How smooth the movement is. 0 = instant, 0.2 = nice and floaty")]
    public float smoothTime = 0.1f;

    [Header("Auto Height Adjustment")]
    [Tooltip("If true, camera will hover above the terrain at this offset")]
    public bool followTerrain = true;
    public float terrainHeight = 10f;
    public LayerMask groundLayer;

    // Internal state
    private Vector3 currentVelocity;
    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        // Lock the cursor to the screen
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Initialize rotation based on the camera's starting orientation
        Vector3 rot = transform.eulerAngles;
        rotationX = rot.y;
        rotationY = rot.x;
    }

    void Update()
    {
        HandleRotation();
        HandleMovement();
    }

    void HandleRotation()
    {
        // Use Right Mouse Button to look around. 
        // This is standard for editor/freecam style cameras.
        if (Input.GetMouseButton(1))
        {
            rotationX += Input.GetAxis("Mouse X") * lookSensitivity;
            rotationY -= Input.GetAxis("Mouse Y") * lookSensitivity;
            rotationY = Mathf.Clamp(rotationY, -89f, 89f);

            transform.rotation = Quaternion.Euler(rotationY, rotationX, 0f);
        }
    }

    void HandleMovement()
    {
        // Get input axis (WASD + QE for vertical)
        float x = Input.GetAxisRaw("Horizontal"); // A, D
        float z = Input.GetAxisRaw("Vertical");   // W, S
        float y = 0f;

        if (Input.GetKey(KeyCode.E)) y += 1f; // E = Up
        if (Input.GetKey(KeyCode.Q)) y -= 1f; // Q = Down

        // Calculate speed
        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift)) speed *= sprintMultiplier;

        // Calculate target velocity
        Vector3 targetVelocity = new Vector3(x, y, z);
        // We multiply the X and Z by the camera's forward/right so "W" moves where you look
        targetVelocity = transform.TransformDirection(targetVelocity) * speed;

        // Smoothly apply the velocity
        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, Time.deltaTime / Mathf.Max(smoothTime, 0.01f));
        transform.position += currentVelocity * Time.deltaTime;

        // Optional: Keep the camera a fixed height above the terrain
        if (followTerrain)
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position + Vector3.up * 1000f, Vector3.down, out hit, Mathf.Infinity, groundLayer))
            {
                Vector3 targetPos = new Vector3(transform.position.x, hit.point.y + terrainHeight, transform.position.z);
                transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * 5f);
            }
        }
    }
}
