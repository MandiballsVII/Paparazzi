using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
public class PlayerGrab : MonoBehaviour
{
    [SerializeField] float grabDistance = 3f;
    [SerializeField] float holdDistance = 1.8f;
    [SerializeField] float throwForce = 12f;
    [SerializeField] float maxGrabMass = 25f;

    PlayerInput playerInput;
    Camera playerCamera;
    CharacterController controller;
    InputAction grabAction;
    Rigidbody heldBody;
    Collider heldCollider;
    bool wasKinematic;
    bool usedGravity;

    public void Initialize(Camera camera)
    {
        playerCamera = camera;
    }

    void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        TryGetComponent(out controller);
    }

    void OnEnable()
    {
        grabAction = playerInput.actions["Interact"];
        grabAction.performed += OnGrabPressed;
        playerInput.actions["Attack"].performed += OnThrowPressed;
    }

    void OnDisable()
    {
        if (grabAction != null)
            grabAction.performed -= OnGrabPressed;

        if (playerInput != null && playerInput.actions != null)
            playerInput.actions["Attack"].performed -= OnThrowPressed;

        Drop(Vector3.zero);
    }

    void OnGrabPressed(InputAction.CallbackContext context)
    {
        if (heldBody != null)
            Drop(Vector3.zero);
        else
            TryGrab();
    }

    void OnThrowPressed(InputAction.CallbackContext context)
    {
        if (heldBody == null)
            return;

        Drop(playerCamera.transform.forward * throwForce);
    }

    void LateUpdate()
    {
        if (heldBody == null || playerCamera == null)
            return;

        Transform cam = playerCamera.transform;
        Vector3 holdPoint = cam.position + cam.forward * holdDistance;
        heldBody.MovePosition(holdPoint);
        heldBody.MoveRotation(cam.rotation);
        heldBody.transform.SetPositionAndRotation(holdPoint, cam.rotation);
    }

    void TryGrab()
    {
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, grabDistance))
            return;

        Rigidbody body = hit.rigidbody;
        if (body == null || body.mass > maxGrabMass)
            return;

        heldBody = body;
        heldCollider = hit.collider;
        wasKinematic = body.isKinematic;
        usedGravity = body.useGravity;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;

        if (controller != null && heldCollider != null)
            Physics.IgnoreCollision(controller, heldCollider, true);
    }

    void Drop(Vector3 impulse)
    {
        if (heldBody != null)
        {
            if (heldCollider != null && controller != null)
                Physics.IgnoreCollision(controller, heldCollider, false);

            heldBody.isKinematic = wasKinematic;
            heldBody.useGravity = usedGravity;

            if (!heldBody.isKinematic && impulse.sqrMagnitude > 0f)
            {
                heldBody.linearVelocity = Vector3.zero;
                heldBody.angularVelocity = Vector3.zero;
                heldBody.AddForce(impulse, ForceMode.VelocityChange);
            }
        }

        heldBody = null;
        heldCollider = null;
    }

    void OnGUI()
    {
        if (playerCamera == null)
            return;

        Rect pixel = playerCamera.pixelRect;
        float x = pixel.x + pixel.width * 0.5f;
        float y = Screen.height - (pixel.y + pixel.height * 0.5f);
        GUI.color = heldBody != null ? Color.yellow : Color.white;
        GUI.Box(new Rect(x - 3f, y - 3f, 6f, 6f), GUIContent.none);
    }
}
