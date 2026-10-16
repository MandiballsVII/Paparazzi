using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] float walkSpeed = 5f;
    [SerializeField] float sprintSpeed = 8f;
    [SerializeField] float jumpHeight = 1.2f;
    [SerializeField] float gravity = -20f;
    [SerializeField] float mouseSensitivity = 0.12f;
    [SerializeField] float stickSensitivity = 120f;
    [SerializeField] float pushPower = 3f;
    [SerializeField] Transform cameraPivot;

    CharacterController controller;
    PlayerInput playerInput;
    InputAction moveAction;
    InputAction lookAction;
    InputAction jumpAction;
    InputAction sprintAction;

    Vector3 velocity;
    float yaw;
    float pitch;
    bool jumpQueued;

    public Camera PlayerCamera { get; private set; }

    public void Initialize(Transform pivot, Camera camera)
    {
        cameraPivot = pivot;
        PlayerCamera = camera;
        yaw = transform.eulerAngles.y;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();
    }

    void OnEnable()
    {
        var actions = playerInput.actions;
        moveAction = actions["Move"];
        lookAction = actions["Look"];
        jumpAction = actions["Jump"];
        sprintAction = actions["Sprint"];
        jumpAction.performed += OnJump;
    }

    void OnDisable()
    {
        if (jumpAction != null)
            jumpAction.performed -= OnJump;
    }

    void OnJump(InputAction.CallbackContext context)
    {
        jumpQueued = true;
    }

    void Update()
    {
        Look();
        Move();
    }

    void Look()
    {
        if (cameraPivot == null)
            return;

        Vector2 look = lookAction.ReadValue<Vector2>();
        bool usingMouse = playerInput.currentControlScheme == "Keyboard&Mouse";
        float scale = usingMouse ? mouseSensitivity : stickSensitivity * Time.deltaTime;

        yaw += look.x * scale;
        pitch -= look.y * scale;
        pitch = Mathf.Clamp(pitch, -85f, 85f);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void Move()
    {
        bool grounded = controller.isGrounded;
        if (grounded && velocity.y < 0f)
            velocity.y = -2f;

        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 wish = transform.right * input.x + transform.forward * input.y;
        if (wish.sqrMagnitude > 1f)
            wish.Normalize();

        float speed = sprintAction.IsPressed() ? sprintSpeed : walkSpeed;
        controller.Move(wish * speed * Time.deltaTime);

        if (jumpQueued && grounded)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        jumpQueued = false;
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.rigidbody;
        if (body == null || body.isKinematic)
            return;

        Vector3 pushDir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        body.AddForce(pushDir * pushPower, ForceMode.Impulse);
    }
}
