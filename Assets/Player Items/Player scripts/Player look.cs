using UnityEngine;

public class Playerlook : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    [Header("Camera Settings")]
    public float mouseSensitivity = 100f;
    [SerializeField] public Transform playerBody;

    private float xRotation = 0f;
    private float yRotation = 0f;
    [Header("Zoom Settings")]
    public Camera playerCamera;
    public float normalFOV = 60f;
    public float zoomFOV = 30f;
    public float zoomSpeed = 5f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked; // Hide cursor
    }

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        //Zooming function call
        HandleZoom();

        // Vertical look (ALWAYS camera)
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -80f, 80f);
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        //Horizontal Rotation
        yRotation += mouseX;
        yRotation = Mathf.Clamp(yRotation, -80f, 50f);

        playerBody.Rotate(Vector3.up * mouseX); 
   
    }
    void HandleZoom()
    {
        float targetFOV;

        if (Input.GetMouseButton(1)) // Right Click Hold
        {
            targetFOV = zoomFOV;
        }
        else
        {
            targetFOV = normalFOV;
        }

        playerCamera.fieldOfView = Mathf.Lerp(
            playerCamera.fieldOfView,
            targetFOV,
            zoomSpeed * Time.deltaTime
        );
    }
}


