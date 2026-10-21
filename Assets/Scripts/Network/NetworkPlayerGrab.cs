using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
public class NetworkPlayerGrab : NetworkBehaviour
{
    [SerializeField]
    float grabDistance = 3f;

    [SerializeField]
    float holdDistance = 1.8f;

    [SerializeField]
    float moveThrowThreshold = 0.75f; // por debajo = dejar en el suelo

    [SerializeField]
    float throwSpeedFactor = 2.5f; // multiplica la velocidad al lanzar

    [SerializeField]
    float minThrowForce = 4f; // suelo del impulso si te mueves

    [SerializeField]
    float maxThrowForce = 18f; // techo

    [SerializeField]
    float maxGrabMass = 25f;

    [SerializeField]
    Camera playerCamera;

    readonly SyncVar<bool> isHolding = new();
    readonly SyncVar<NetworkObject> heldSync = new();

    PlayerInput playerInput;
    PlayerPauseMenu pauseMenu;
    CharacterController controller;
    InputAction grabAction;
    bool inputBound;

    NetworkObject heldObject;
    Collider heldCollider;
    bool wasKinematic;
    bool usedGravity;

    void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        TryGetComponent(out controller);
        TryGetComponent(out pauseMenu);

        if (playerCamera == null)
        {
            Transform pivot = transform.Find("CameraPivot");
            if (pivot != null)
                playerCamera = pivot.GetComponentInChildren<Camera>(true);
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner)
            BindInput();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        UnbindInput();
        if (IsServerInitialized)
            ForceDrop(Vector3.zero);
    }

    void BindInput()
    {
        if (inputBound || playerInput == null || playerInput.actions == null)
            return;

        grabAction = playerInput.actions["Interact"];
        grabAction.performed += OnGrabPressed;
        inputBound = true;
    }

    void UnbindInput()
    {
        if (!inputBound)
            return;

        if (grabAction != null)
            grabAction.performed -= OnGrabPressed;
        inputBound = false;
    }

    void OnGrabPressed(InputAction.CallbackContext context)
{
    if (!IsOwner)
        return;

    if (pauseMenu != null && pauseMenu.IsOpen)
        return;

    if (isHolding.Value)
    {
        DropServerRpc(CalculateReleaseImpulse());
        return;
    }

    if (playerCamera == null)
        return;

    Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
    if (!Physics.Raycast(ray, out RaycastHit hit, grabDistance))
        return;

    if (!hit.rigidbody || hit.rigidbody.mass > maxGrabMass)
        return;

    if (!hit.rigidbody.TryGetComponent(out NetworkObject nob) || !nob.IsSpawned)
        return;

    GrabServerRpc(nob);
}

Vector3 CalculateReleaseImpulse()
{
    if (controller == null || playerCamera == null)
        return Vector3.zero;

    Vector3 planar = controller.velocity;
    planar.y = 0f;
    float speed = planar.magnitude;

    if (speed < moveThrowThreshold)
        return Vector3.zero;

    float strength = Mathf.Clamp(speed * throwSpeedFactor, minThrowForce, maxThrowForce);
    return playerCamera.transform.forward * strength;
}

    [ServerRpc]
    void GrabServerRpc(NetworkObject target, NetworkConnection caller = null)
    {
        if (target == null || !target.IsSpawned)
            return;

        if (!target.TryGetComponent(out Rigidbody body) || body.mass > maxGrabMass)
            return;

        if (heldObject != null)
            ForceDrop(Vector3.zero);

        heldObject = target;
        heldSync.Value = target;
        wasKinematic = body.isKinematic;
        usedGravity = body.useGravity;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;

        if (controller != null && body.TryGetComponent(out heldCollider))
            Physics.IgnoreCollision(controller, heldCollider, true);

        isHolding.Value = true;

        if (TryGetHoldPose(out Vector3 pos, out Quaternion rot))
            ApplyHoldPose(body, pos, rot);
    }

    [ServerRpc]
    void DropServerRpc(Vector3 impulse)
    {
        ForceDrop(impulse);
    }

    [ServerRpc]
    void UpdateHoldServerRpc(Vector3 position, Quaternion rotation)
    {
        if (heldObject == null || !heldObject.TryGetComponent(out Rigidbody body))
            return;

        ApplyHoldPose(body, position, rotation);
    }

    void ForceDrop(Vector3 impulse)
    {
        if (heldObject != null && heldObject.TryGetComponent(out Rigidbody body))
        {
            if (controller != null && heldCollider != null)
                Physics.IgnoreCollision(controller, heldCollider, false);

            body.isKinematic = wasKinematic;
            body.useGravity = usedGravity;

            if (!body.isKinematic && impulse.sqrMagnitude > 0f)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.AddForce(impulse, ForceMode.VelocityChange);
            }
        }

        heldObject = null;
        heldCollider = null;
        if (IsServerInitialized)
        {
            heldSync.Value = default;
            isHolding.Value = false;
        }
    }

    void LateUpdate()
    {
        if (!IsOwner || !isHolding.Value)
            return;

        if (pauseMenu != null && pauseMenu.IsOpen)
            return;

        if (!TryGetHoldPose(out Vector3 pos, out Quaternion rot))
            return;

        // Pose real de cámara (yaw + pitch) al servidor + predicción local.
        UpdateHoldServerRpc(pos, rot);
        ApplyHoldLocally(pos, rot);
    }

    void ApplyHoldLocally(Vector3 position, Quaternion rotation)
    {
        NetworkObject target = heldSync.Value;
        if (target == null || !target.TryGetComponent(out Rigidbody body))
            return;

        ApplyHoldPose(body, position, rotation);
    }

    static void ApplyHoldPose(Rigidbody body, Vector3 position, Quaternion rotation)
    {
        body.MovePosition(position);
        body.MoveRotation(rotation);
        body.transform.SetPositionAndRotation(position, rotation);
    }

    bool TryGetHoldPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = default;

        if (playerCamera == null)
            return false;

        // En servidor, para jugadores remotos la cámara no tiene pitch; usamos forward del transform.
        if (!IsOwner && IsServerInitialized)
        {
            position = transform.position + Vector3.up * 0.6f + transform.forward * holdDistance;
            rotation = transform.rotation;
            return true;
        }

        Transform cam = playerCamera.transform;
        position = cam.position + cam.forward * holdDistance;
        rotation = cam.rotation;
        return true;
    }

    void OnGUI()
    {
        if (!IsOwner || playerCamera == null)
            return;

        Rect pixel = playerCamera.pixelRect;
        float x = pixel.x + pixel.width * 0.5f;
        float y = Screen.height - (pixel.y + pixel.height * 0.5f);
        GUI.color = isHolding.Value ? Color.yellow : Color.white;
        GUI.Box(new Rect(x - 3f, y - 3f, 6f, 6f), GUIContent.none);
    }

    void OnDisable()
    {
        UnbindInput();
    }
}
