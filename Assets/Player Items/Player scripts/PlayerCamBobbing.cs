using UnityEngine;

public class CameraHeadBob : MonoBehaviour
{
    public Transform cameraTransform;
    public PlayerMovement movementScript; // Reference to PlayerMovement script

    [Header("Walk Bobbing")]
    public float walkBobSpeed = 6f;
    public float walkBobAmount = 0.05f;

    [Header("Sprint Bobbing")]
    public float sprintBobSpeed = 10f;
    public float sprintBobAmount = 0.1f;

    [Header("Idle Breathing")]
    public float idleBreathSpeed = 1f;
    public float idleBreathAmount = 0.02f;

    private float defaultY;
    private float timer;

    void Start()
    {
        if (cameraTransform == null) cameraTransform = transform;
        defaultY = cameraTransform.localPosition.y;
    }

    void Update()
    {
        float bobSpeed, bobAmount;

        if (movementScript.IsCrouching)
        {
            // No bobbing while sitting
            cameraTransform.localPosition = new Vector3(cameraTransform.localPosition.x, defaultY, cameraTransform.localPosition.z);
            return;
        }

        if (movementScript.IsMoving)
        {
            // Sprinting or walking?
            if (movementScript.IsSprinting)
            {
                bobSpeed = sprintBobSpeed;
                bobAmount = sprintBobAmount;
            }
            else
            {
                bobSpeed = walkBobSpeed;
                bobAmount = walkBobAmount;
            }

            timer += Time.deltaTime * bobSpeed;
            float newY = defaultY + Mathf.Sin(timer) * bobAmount;
            cameraTransform.localPosition = new Vector3(cameraTransform.localPosition.x, newY, cameraTransform.localPosition.z);
        }
        else
        {
            timer += Time.deltaTime * idleBreathSpeed;
            float newY = defaultY + Mathf.Sin(timer) * idleBreathAmount;
            cameraTransform.localPosition = new Vector3(cameraTransform.localPosition.x, newY, cameraTransform.localPosition.z);
        }
    }
}
