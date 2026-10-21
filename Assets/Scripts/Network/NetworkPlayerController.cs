using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class NetworkPlayerController : NetworkBehaviour
{
    [SerializeField]
    float walkSpeed = 5f;

    [SerializeField]
    float sprintSpeed = 8f;

    [SerializeField]
    float jumpHeight = 1.2f;

    [SerializeField]
    float gravity = -20f;

    [SerializeField]
    float mouseSensitivity = 0.12f;

    [SerializeField]
    float stickSensitivity = 120f;

    [SerializeField]
    float pushPower = 3f;

    [SerializeField]
    Transform cameraPivot;

    [SerializeField]
    Camera playerCamera;

    [SerializeField]
    Transform faceIndicator;

    readonly SyncVar<int> colorIndex = new();
    readonly SyncVar<bool> isCrouching = new();

    CharacterController controller;
    PlayerInput playerInput;
    PlayerPauseMenu pauseMenu;
    PlayerStance stance;
    InputAction moveAction;
    InputAction lookAction;
    InputAction jumpAction;
    InputAction sprintAction;
    InputAction crouchAction;

    Vector3 velocity;
    float yaw;
    float pitch;
    bool jumpQueued;
    bool inputBound;

    public Camera PlayerCamera => playerCamera;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();
        EnsurePhotoSystems();
        TryGetComponent(out pauseMenu);
        AlignCapsuleWithMesh();
        EnsureFaceIndicator();
        EnsureStance();

        if (cameraPivot == null)
        {
            Transform found = transform.Find("CameraPivot");
            if (found != null)
                cameraPivot = found;
        }

        if (stance != null)
            stance.Initialize(cameraPivot);
        else if (cameraPivot != null)
            cameraPivot.localPosition = new Vector3(0f, 0.75f, 0f);

        if (playerCamera == null && cameraPivot != null)
            playerCamera = cameraPivot.GetComponentInChildren<Camera>(true);

        if (playerCamera != null)
        {
            playerCamera.enabled = false;
            if (playerCamera.TryGetComponent(out AudioListener listener))
                listener.enabled = false;
        }

        if (playerInput != null)
            playerInput.enabled = false;
    }

    void AlignCapsuleWithMesh()
    {
        if (controller == null)
            return;

        // Mesh Capsule por defecto: alto 2, radio 0.5, centrada en el origen.
        controller.height = 2f;
        controller.radius = 0.5f;
        controller.center = Vector3.zero;
        controller.skinWidth = 0.08f;
    }

    void EnsureFaceIndicator()
    {
        if (faceIndicator == null)
        {
            Transform existing = transform.Find("Face");
            if (existing != null)
                faceIndicator = existing;
        }

        if (faceIndicator != null)
        {
            faceIndicator.localPosition = ArenaBuilder.FacingMarkerLocalPosition;
            return;
        }

        GameObject face = GameObject.CreatePrimitive(PrimitiveType.Cube);
        face.name = "Face";
        if (face.TryGetComponent(out Collider faceCollider))
            Destroy(faceCollider);

        face.transform.SetParent(transform, false);
        // Marcador de orientación a la altura de los pies (no tapa la cámara).
        face.transform.localPosition = ArenaBuilder.FacingMarkerLocalPosition;
        face.transform.localScale = new Vector3(0.35f, 0.22f, 0.12f);
        faceIndicator = face.transform;

        ArenaBuilder.ApplyColor(face, new Color(0.08f, 0.08f, 0.1f));
    }

    void EnsureStance()
    {
        if (!TryGetComponent(out stance))
            stance = gameObject.AddComponent<PlayerStance>();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        colorIndex.OnChange += OnColorIndexChanged;
        isCrouching.OnChange += OnCrouchingChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        colorIndex.OnChange -= OnColorIndexChanged;
        isCrouching.OnChange -= OnCrouchingChanged;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        int index = Owner.ClientId >= 0 ? Owner.ClientId % ArenaBuilder.PlayerColors.Length : 0;
        colorIndex.Value = index;
        ApplyColor(index);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyColor(colorIndex.Value);
        if (stance != null)
            stance.SetCrouching(isCrouching.Value);

        if (!IsOwner)
        {
            if (controller != null)
                controller.enabled = false;

            if (playerCamera != null)
            {
                playerCamera.enabled = false;
                if (playerCamera.TryGetComponent(out AudioListener listener))
                    listener.enabled = false;
            }

            return;
        }

        if (controller != null)
            controller.enabled = true;

        EnableOwnerControls();
    }

    void EnableOwnerControls()
    {
        yaw = transform.eulerAngles.y;

        if (playerCamera != null)
        {
            playerCamera.enabled = true;
            playerCamera.rect = new Rect(0f, 0f, 1f, 1f);
            if (playerCamera.TryGetComponent(out AudioListener listener))
                listener.enabled = true;
            if (playerCamera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            {
                cameraData.renderType = CameraRenderType.Base;
                cameraData.renderPostProcessing = false;
            }
        }

        if (playerInput != null)
        {
            if (playerInput.actions == null)
                playerInput.actions = InputSystem.actions;

            playerInput.defaultActionMap = "Player";
            playerInput.neverAutoSwitchControlSchemes = false;
            playerInput.enabled = true;
            BindInput();
        }

        if (TryGetComponent(out PlayerPhotoCapture capture))
        {
            capture.Initialize(playerCamera);
            capture.BindInput();
        }

        if (pauseMenu != null)
        {
            pauseMenu.Initialize(playerCamera);
            pauseMenu.BindPause();
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void EnsurePhotoSystems()
    {
        if (!TryGetComponent(out PlayerPhotoAlbum _))
            gameObject.AddComponent<PlayerPhotoAlbum>();

        if (!TryGetComponent(out PlayerPhotoCapture _))
            gameObject.AddComponent<PlayerPhotoCapture>();

        if (!TryGetComponent(out PlayerPauseMenu menu))
            menu = gameObject.AddComponent<PlayerPauseMenu>();

        pauseMenu = menu;
    }

    void BindInput()
    {
        if (inputBound || playerInput == null || playerInput.actions == null)
            return;

        moveAction = playerInput.actions["Move"];
        lookAction = playerInput.actions["Look"];
        jumpAction = playerInput.actions["Jump"];
        sprintAction = playerInput.actions["Sprint"];
        crouchAction = playerInput.actions["Crouch"];
        jumpAction.performed += OnJump;
        inputBound = true;
    }

    void UnbindInput()
    {
        if (!inputBound || jumpAction == null)
            return;

        jumpAction.performed -= OnJump;
        inputBound = false;
    }

    void OnJump(InputAction.CallbackContext context)
    {
        if (!IsOwner)
            return;

        jumpQueued = true;
    }

    void Update()
    {
        if (!IsOwner)
            return;

        if (pauseMenu != null && pauseMenu.IsOpen)
            return;

        UpdateCrouch();
        Look();
        Move();
    }

    void UpdateCrouch()
    {
        if (crouchAction == null || stance == null)
            return;

        bool wantCrouch = crouchAction.IsPressed();
        if (wantCrouch == stance.IsCrouching && wantCrouch == isCrouching.Value)
            return;

        bool before = stance.IsCrouching;
        stance.SetCrouching(wantCrouch);
        // Si no puede levantarse (techo), no mandamos el SyncVar.
        if (stance.IsCrouching == before && wantCrouch != before)
            return;

        if (stance.IsCrouching != isCrouching.Value)
            SetCrouchingServerRpc(stance.IsCrouching);
    }

    void Look()
    {
        if (cameraPivot == null || lookAction == null)
            return;

        Vector2 look = lookAction.ReadValue<Vector2>();
        bool usingMouse =
            playerInput.currentControlScheme == "Keyboard&Mouse"
            || playerInput.currentControlScheme == null;
        float scale = usingMouse ? mouseSensitivity : stickSensitivity * Time.deltaTime;

        yaw += look.x * scale;
        pitch -= look.y * scale;
        pitch = Mathf.Clamp(pitch, -85f, 85f);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void Move()
    {
        if (moveAction == null || sprintAction == null)
            return;

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

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!IsOwner)
            return;

        Rigidbody body = hit.rigidbody;
        if (body == null || body.isKinematic)
            return;

        if (!body.TryGetComponent(out NetworkObject nob) || !nob.IsSpawned)
            return;

        Vector3 pushDir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        if (pushDir.sqrMagnitude < 0.001f)
            return;

        PushObjectServerRpc(nob, pushDir.normalized * pushPower);
    }

    [ServerRpc]
    void PushObjectServerRpc(NetworkObject target, Vector3 force)
    {
        if (target == null || !target.TryGetComponent(out Rigidbody body) || body.isKinematic)
            return;

        body.AddForce(force, ForceMode.Impulse);
    }

    [ServerRpc]
    void SetCrouchingServerRpc(bool crouch)
    {
        isCrouching.Value = crouch;
    }

    void OnCrouchingChanged(bool prev, bool next, bool asServer)
    {
        if (stance == null)
            TryGetComponent(out stance);

        // ignoreCeiling: la autoridad del techo la tiene el owner al enviar el Rpc.
        if (stance != null)
            stance.SetCrouching(next, ignoreCeiling: true);
    }

    void OnColorIndexChanged(int prev, int next, bool asServer)
    {
        ApplyColor(next);
    }

    void ApplyColor(int index)
    {
        index = Mathf.Clamp(index, 0, ArenaBuilder.PlayerColors.Length - 1);
        ArenaBuilder.ApplyColor(gameObject, ArenaBuilder.PlayerColors[index]);
    }

    void OnDisable()
    {
        UnbindInput();
    }
}
