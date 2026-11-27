using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using FishNet.Component.Transforming;

/// <summary>
/// Capa de red del player: ownership, SyncVars, soft-push ServerRpc.
/// El motor lo lleva <see cref="PlayerMovement"/> (mismo en local/Gym/online).
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(PlayerMovement))]
public class NetworkPlayerController : NetworkBehaviour
{
    [SerializeField]
    Transform cameraPivot;

    [SerializeField]
    Camera playerCamera;

    [SerializeField]
    Transform faceIndicator;

    [SerializeField]
    float softPushAcceleration = 14f;

    [SerializeField]
    float maxSoftPushSpeed = 3f;

    readonly SyncVar<int> colorIndex = new();
    readonly SyncVar<bool> isCrouching = new();

    CharacterController controller;
    PlayerInput playerInput;
    PlayerMovement movement;
    PlayerPauseMenu pauseMenu;
    PlayerStance stance;
    PlayerHealth health;
    bool offlineLocalControl;
    bool crouchHooked;

    public Camera PlayerCamera => playerCamera;
    public bool HasLocalControl => offlineLocalControl || (IsSpawned && IsOwner);

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();
        EnsureMovement();
        EnsurePhotoSystems();
        EnsureHealthSystems();
        EnsurePerceptionEmitters();
        TryGetComponent(out pauseMenu);
        TryGetComponent(out health);
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

        // Hasta OnStartClient / offline: el motor no debe conducir.
        if (movement != null)
            movement.MotorActive = false;
    }

    void EnsureMovement()
    {
        if (!TryGetComponent(out movement))
            movement = gameObject.AddComponent<PlayerMovement>();
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
        UnhookMovement();
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

            if (movement != null)
            {
                movement.MotorActive = false;
                movement.enabled = false;
            }

            if (playerCamera != null)
            {
                playerCamera.enabled = false;
                if (playerCamera.TryGetComponent(out AudioListener listener))
                    listener.enabled = false;
            }

            if (TryGetComponent(out PlayerHealthHud remoteHud))
                remoteHud.SetLocalHudAllowed(false);

            if (TryGetComponent(out PlayerAlarmHud remoteAlarmHud))
                remoteAlarmHud.SetLocalHudAllowed(false);

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

        localGrab.ClearNetworkHandlers();
        localGrab.GrabActive = true;

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
        EnsureMovement();

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
        }

        if (movement != null)
        {
            movement.enabled = true;
            movement.Initialize(cameraPivot, playerCamera);
            movement.SoftPushHandler = HandleSoftPushFromMotor;
            HookMovementCrouch();
            movement.MotorActive = true;
        }

        if (TryGetComponent(out PlayerPhotoCapture capture))
        {
            capture.Initialize(playerCamera);
            capture.RebindActionsForPrivateAsset();
        }

        if (pauseMenu != null)
        {
            pauseMenu.Initialize(playerCamera);
            pauseMenu.RebindActionsForPrivateAsset();
        }

        if (TryGetComponent(out PlayerHealthHud hud))
        {
            hud.SetLocalHudAllowed(true);
            hud.Initialize(playerCamera);
        }

        if (TryGetComponent(out PlayerAlarmHud alarmHud))
        {
            alarmHud.SetLocalHudAllowed(true);
            alarmHud.Initialize();
        }
        else
        {
            Debug.LogError(
                "[NetworkPlayer] Falta PlayerAlarmHud en el prefab. " +
                "Tools → Paparazzi → Setup NetworkPlayer Alarm HUD.");
        }

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

    void HookMovementCrouch()
    {
        if (movement == null || crouchHooked)
            return;

        movement.CrouchChanged += OnMotorCrouchChanged;
        crouchHooked = true;
    }

    void UnhookMovement()
    {
        if (movement != null && crouchHooked)
        {
            movement.CrouchChanged -= OnMotorCrouchChanged;
            movement.SoftPushHandler = null;
            movement.MotorActive = false;
        }

        crouchHooked = false;
    }

    void OnMotorCrouchChanged(bool crouching)
    {
        if (offlineLocalControl || !IsSpawned || !IsOwner)
            return;

        if (isCrouching.Value != crouching)
            SetCrouchingServerRpc(crouching);
    }

    void HandleSoftPushFromMotor(Rigidbody body, Vector3 point)
    {
        if (body == null || body.isKinematic)
            return;

        if (offlineLocalControl || !IsSpawned)
        {
            PlayerSoftPush.SoftPushDynamicBody(body, point, softPushAcceleration, maxSoftPushSpeed);
            return;
        }

        if (!body.TryGetComponent(out NetworkObject nob) || !nob.IsSpawned)
            return;

        SoftPushObjectServerRpc(nob, point);
    }

    void EnsurePhotoSystems()
    {
        if (!TryGetComponent(out PlayerPhotoAlbum _))
            gameObject.AddComponent<PlayerPhotoAlbum>();

        if (!TryGetComponent(out PlayerPhotoCapture _))
            gameObject.AddComponent<PlayerPhotoCapture>();

        if (!TryGetComponent(out PlayerPauseMenu menu))
            menu = gameObject.AddComponent<PlayerPauseMenu>();

        if (!TryGetComponent(out PhotoEconomyReporter _))
            Debug.LogError("[NetworkPlayer] Falta PhotoEconomyReporter en el prefab NetworkPlayer.");

        pauseMenu = menu;
    }

    void EnsureHealthSystems()
    {
        if (!TryGetComponent(out health))
        {
            Debug.LogError("[NetworkPlayer] Falta PlayerHealth en el prefab.");
            health = gameObject.AddComponent<PlayerHealth>();
        }

        if (!TryGetComponent(out NetworkPlayerHealth _))
            Debug.LogError(
                "[NetworkPlayer] Falta NetworkPlayerHealth en el prefab. " +
                "Tools → Paparazzi → Setup NetworkPlayer Health Network.");

        if (!TryGetComponent(out PlayerRagdoll _))
            gameObject.AddComponent<PlayerRagdoll>();

        if (!TryGetComponent(out PlayerHealthHud _))
            Debug.LogError(
                "[NetworkPlayer] Falta PlayerHealthHud en el prefab. " +
                "Tools → Paparazzi → Setup NetworkPlayer Health HUD.");
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
        UnhookMovement();
    }
}
