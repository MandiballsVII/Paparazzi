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
    [SerializeField] float softPushAcceleration = 14f;
    [SerializeField] float maxSoftPushSpeed = 3f;
    [SerializeField] float playerSeparateSpeed = 3.5f;
    [SerializeField] Transform cameraPivot;

    CharacterController controller;
    PlayerInput playerInput;
    PlayerPauseMenu pauseMenu;
    PlayerStance stance;
    PlayerHealth health;
    PlayerGrab grab;
    InputAction moveAction;
    InputAction lookAction;
    InputAction jumpAction;
    InputAction sprintAction;
    InputAction crouchAction;

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

        if (!TryGetComponent(out stance))
            stance = gameObject.AddComponent<PlayerStance>();
        stance.Initialize(pivot);
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();
        TryGetComponent(out pauseMenu);
        TryGetComponent(out stance);
        TryGetComponent(out health);
        TryGetComponent(out grab);
    }

    void OnEnable()
    {
        var actions = playerInput.actions;
        moveAction = actions["Move"];
        lookAction = actions["Look"];
        jumpAction = actions["Jump"];
        sprintAction = actions["Sprint"];
        crouchAction = actions["Crouch"];
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
        if (pauseMenu == null)
            TryGetComponent(out pauseMenu);
        if (health == null)
            TryGetComponent(out health);

        if (health != null && health.IsDead)
            return;

        if (pauseMenu != null && pauseMenu.IsOpen)
            return;

        UpdateCrouch();
        Look();
        Move();
        SeparateFromPlayers();
    }

    void UpdateCrouch()
    {
        if (stance == null)
            TryGetComponent(out stance);
        if (stance == null || crouchAction == null)
            return;

        stance.SetCrouching(crouchAction.IsPressed());
    }

    void Look()
    {
        if (cameraPivot == null)
            return;

        Vector2 look = lookAction.ReadValue<Vector2>();
        bool usingMouse = playerInput.currentControlScheme == "Keyboard&Mouse";
        float scale = usingMouse ? mouseSensitivity : stickSensitivity * Time.deltaTime;

        float previousYaw = yaw;
        float previousPitch = pitch;
        yaw += look.x * scale;
        pitch -= look.y * scale;
        pitch = Mathf.Clamp(pitch, -85f, 85f);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        if (grab == null)
            TryGetComponent(out grab);
        if (grab != null && grab.IsHolding)
            grab.ConstrainLook(ref yaw, ref pitch, previousYaw, previousPitch, transform, cameraPivot);
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

        bool crouching = stance != null && stance.IsCrouching;
        float speed = (!crouching && sprintAction.IsPressed()) ? sprintSpeed : walkSpeed;
        if (stance != null)
            speed *= stance.MoveMultiplier;

        controller.Move(wish * speed * Time.deltaTime);

        if (jumpQueued && grounded && !crouching)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        jumpQueued = false;
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    void SeparateFromPlayers()
    {
        float probe = controller.radius + 0.35f;
        PlayerSoftPush.SeparateFromNearbyPlayers(controller, probe, playerSeparateSpeed, Time.deltaTime);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.rigidbody;
        if (body == null || body.isKinematic)
            return;

        Vector3 pushDir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        if (pushDir.sqrMagnitude < 0.0001f)
            return;

        PlayerSoftPush.SoftPushDynamicBody(
            body,
            hit.point,
            softPushAcceleration,
            maxSoftPushSpeed);
    }
}
