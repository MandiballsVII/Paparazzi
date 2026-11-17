using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using FishNet.Component.Transforming;

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
    float softPushAcceleration = 14f;

    [SerializeField]
    float maxSoftPushSpeed = 3f;

    [SerializeField]
    float playerSeparateSpeed = 3.5f;

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
    PlayerHealth health;
    NetworkPlayerGrab grab;
    InputAction moveAction;
    InputAction lookAction;
    InputAction jumpAction;
    InputAction sprintAction;
    InputAction crouchAction;
    InputAction crouchToggleAction;

    Vector3 velocity;
    float yaw;
    float pitch;
    bool jumpQueued;
    bool inputBound;
    bool crouchToggleLatched;
    bool offlineLocalControl;

    public Camera PlayerCamera => playerCamera;
    public bool HasLocalControl => offlineLocalControl || (IsSpawned && IsOwner);

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();
        EnsurePhotoSystems();
        EnsureHealthSystems();
        EnsurePerceptionEmitters();
        TryGetComponent(out pauseMenu);
        TryGetComponent(out health);
        TryGetComponent(out grab);
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
            cameraPivot.localPosition = PlayerBodyMetrics.CameraPivotLocalPosition;

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

        PlayerBodyMetrics.ApplyToCharacterController(controller);
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

    void EnsurePerceptionEmitters()
    {
        if (!TryGetComponent(out PerceptionTarget _))
            gameObject.AddComponent<PerceptionTarget>();

        if (!TryGetComponent(out FootstepNoiseEmitter _))
            gameObject.AddComponent<FootstepNoiseEmitter>();
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

        if (offlineLocalControl)
            return;

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

    /// <summary>
    /// Gym / tests sin FishNet: misma cápsula/cámara/mecánicas del NetworkPlayer.
    /// </summary>
    public void ActivateOfflineLocalControl(Rect viewport, int colorIndexValue = 0)
    {
        offlineLocalControl = true;

        if (TryGetComponent(out NetworkTransform networkTransform))
            networkTransform.enabled = false;

        if (TryGetComponent(out NetworkPlayerGrab networkGrab))
            networkGrab.enabled = false;

        if (!TryGetComponent(out PlayerGrab localGrab))
            localGrab = gameObject.AddComponent<PlayerGrab>();

        if (controller != null)
            controller.enabled = true;

        EnableOwnerControls();

        if (playerCamera != null)
            playerCamera.rect = viewport;

        localGrab.Initialize(playerCamera);
        ApplyColor(colorIndexValue);

        LobbyPlayerTransfer.EnsureIdentity(
            gameObject,
            colorIndexValue,
            colorIndexValue,
            viewport,
            playerInput != null ? playerInput.currentControlScheme : string.Empty);
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
            PlayerInputUtility.EnsurePrivateActionAsset(playerInput);

            playerInput.defaultActionMap = "Player";
            if (!offlineLocalControl)
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

        if (TryGetComponent(out PlayerHealthHud hud))
            hud.Initialize(playerCamera);

        if (offlineLocalControl)
            return;

        int index = Owner.ClientId >= 0 ? Owner.ClientId : 0;
        int color = colorIndex.Value;
        LobbyPlayerTransfer.EnsureIdentity(
            gameObject,
            index,
            color,
            playerCamera != null ? playerCamera.rect : new Rect(0f, 0f, 1f, 1f),
            playerInput != null ? playerInput.currentControlScheme : string.Empty);

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

    void EnsureHealthSystems()
    {
        if (!TryGetComponent(out health))
            health = gameObject.AddComponent<PlayerHealth>();

        if (!TryGetComponent(out NetworkPlayerHealth _))
            gameObject.AddComponent<NetworkPlayerHealth>();

        if (!TryGetComponent(out PlayerRagdoll _))
            gameObject.AddComponent<PlayerRagdoll>();

        if (!TryGetComponent(out PlayerHealthHud _))
            gameObject.AddComponent<PlayerHealthHud>();
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
        crouchToggleAction = playerInput.actions.FindAction("CrouchToggle", false);
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
        if (!HasLocalControl)
            return;

        jumpQueued = true;
    }

    void Update()
    {
        if (!HasLocalControl)
            return;

        if (health == null)
            TryGetComponent(out health);
        if (health != null && health.IsDead)
        {
            crouchToggleLatched = false;
            return;
        }

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
            return;

        if (crouchToggleAction != null && crouchToggleAction.WasPressedThisFrame())
            crouchToggleLatched = !crouchToggleLatched;

        bool holdCrouch = crouchAction != null && crouchAction.IsPressed();
        bool wantCrouch = holdCrouch || crouchToggleLatched;

        if (offlineLocalControl)
        {
            if (wantCrouch == stance.IsCrouching)
                return;

            bool beforeOffline = stance.IsCrouching;
            stance.SetCrouching(wantCrouch);
            if (!wantCrouch && stance.IsCrouching && beforeOffline)
                crouchToggleLatched = true;
            return;
        }

        if (wantCrouch == stance.IsCrouching && wantCrouch == isCrouching.Value)
            return;

        bool before = stance.IsCrouching;
        stance.SetCrouching(wantCrouch);
        // Si no puede levantarse (techo), no mandamos el SyncVar y mantenemos latch.
        if (stance.IsCrouching == before && wantCrouch != before)
        {
            if (!wantCrouch && stance.IsCrouching)
                crouchToggleLatched = true;
            return;
        }

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

    void SeparateFromPlayers()
    {
        float probe = controller.radius + 0.35f;
        PlayerSoftPush.SeparateFromNearbyPlayers(controller, probe, playerSeparateSpeed, Time.deltaTime);
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
        if (!HasLocalControl)
            return;

        Rigidbody body = hit.rigidbody;
        if (body == null || body.isKinematic)
            return;

        if (offlineLocalControl || !IsSpawned)
        {
            PlayerSoftPush.SoftPushDynamicBody(
                body,
                hit.point,
                softPushAcceleration,
                maxSoftPushSpeed);
            return;
        }

        if (!body.TryGetComponent(out NetworkObject nob) || !nob.IsSpawned)
            return;

        SoftPushObjectServerRpc(nob, hit.point);
    }

    [ServerRpc]
    void SoftPushObjectServerRpc(NetworkObject target, Vector3 fromPoint)
    {
        if (target == null || !target.TryGetComponent(out Rigidbody body) || body.isKinematic)
            return;

        PlayerSoftPush.SoftPushDynamicBody(body, fromPoint, softPushAcceleration, maxSoftPushSpeed);
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
