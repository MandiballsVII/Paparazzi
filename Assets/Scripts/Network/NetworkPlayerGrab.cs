using System.Collections.Generic;
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
    float moveThrowThreshold = 0.75f;

    [SerializeField]
    float throwSpeedFactor = 2.5f;

    [SerializeField]
    float minThrowForce = 4f;

    [SerializeField]
    float maxThrowForce = 18f;

    [SerializeField]
    float maxGrabMass = 25f;

    [Header("Hold Collision")]
    [SerializeField]
    int lookSearchIterations = 10;

    [SerializeField]
    float softPushAcceleration = 16f;

    [SerializeField]
    float maxSoftPushSpeed = 3f;

    [SerializeField]
    Camera playerCamera;

    readonly SyncVar<bool> isHolding = new();
    readonly SyncVar<NetworkObject> heldSync = new();

    PlayerInput playerInput;
    PlayerPauseMenu pauseMenu;
    PlayerHealth health;
    CharacterController controller;
    InputAction grabAction;
    bool inputBound;

    NetworkObject heldObject;
    Collider heldCollider;
    bool wasKinematic;
    bool usedGravity;
    readonly List<Collider> ignoredColliders = new();
    static readonly Collider[] softPushBuffer = new Collider[16];
    float nextIgnoreRefreshTime;

    public bool IsHolding => isHolding.Value;

    void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        TryGetComponent(out controller);
        TryGetComponent(out pauseMenu);
        TryGetComponent(out health);

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
        ClearIgnoredCollisions();
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

        if (health == null)
            TryGetComponent(out health);
        if (health != null && health.IsDead)
            return;

        if (isHolding.Value)
        {
            DropServerRpc(CalculateReleaseImpulse());
            return;
        }

        if (playerCamera == null)
            return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, grabDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        if (!hit.rigidbody)
            return;

        bool isDeadPlayer = hit.rigidbody.TryGetComponent(out PlayerHealth targetHealth) && targetHealth.IsDead;
        if (!isDeadPlayer && hit.rigidbody.mass > maxGrabMass)
            return;

        if (!hit.rigidbody.TryGetComponent(out NetworkObject nob) || !nob.IsSpawned)
            return;

        if (nob == NetworkObject)
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

    public void ConstrainLook(
        ref float yaw,
        ref float pitch,
        float previousYaw,
        float previousPitch,
        Transform body,
        Transform cameraPivot)
    {
        if (!IsHolding || playerCamera == null || body == null || cameraPivot == null)
            return;

        if (!TryGetHeldRoot(out Transform heldRoot))
            return;

        if (heldCollider == null)
            heldRoot.TryGetComponent(out heldCollider);
        if (heldCollider == null)
            return;

        if (Mathf.Abs(Mathf.DeltaAngle(previousYaw, yaw)) < 0.0001f
            && Mathf.Abs(pitch - previousPitch) < 0.0001f)
            return;

        ApplyLook(body, cameraPivot, yaw, pitch);
        if (!IsCurrentHoldBlocked(heldRoot))
            return;

        float lowYaw = previousYaw;
        float lowPitch = previousPitch;
        float highYaw = yaw;
        float highPitch = pitch;

        for (int i = 0; i < lookSearchIterations; i++)
        {
            float midYaw = Mathf.LerpAngle(lowYaw, highYaw, 0.5f);
            float midPitch = Mathf.Lerp(lowPitch, highPitch, 0.5f);
            ApplyLook(body, cameraPivot, midYaw, midPitch);

            if (IsCurrentHoldBlocked(heldRoot))
            {
                highYaw = midYaw;
                highPitch = midPitch;
            }
            else
            {
                lowYaw = midYaw;
                lowPitch = midPitch;
            }
        }

        yaw = lowYaw;
        pitch = lowPitch;
        ApplyLook(body, cameraPivot, yaw, pitch);
    }

    static void ApplyLook(Transform body, Transform cameraPivot, float yaw, float pitch)
    {
        body.rotation = Quaternion.Euler(0f, yaw, 0f);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    bool IsCurrentHoldBlocked(Transform heldRoot)
    {
        if (playerCamera == null || heldCollider == null || heldRoot == null)
            return false;

        Transform cam = playerCamera.transform;
        Vector3 holdPoint = cam.position + cam.forward * holdDistance;
        return HeldObjectHoldQuery.IsPoseBlocked(
            heldCollider,
            heldRoot,
            transform,
            holdPoint,
            cam.rotation);
    }

    bool TryGetHeldRoot(out Transform heldRoot)
    {
        heldRoot = null;
        if (heldSync.Value != null)
            heldRoot = heldSync.Value.transform;
        else if (heldObject != null)
            heldRoot = heldObject.transform;
        return heldRoot != null;
    }

    [ServerRpc]
    void GrabServerRpc(NetworkObject target, NetworkConnection caller = null)
    {
        if (target == null || !target.IsSpawned)
            return;

        if (!target.TryGetComponent(out Rigidbody body))
            return;

        bool isDeadPlayer = target.TryGetComponent(out PlayerHealth targetHealth) && targetHealth.IsDead;
        if (!isDeadPlayer && body.mass > maxGrabMass)
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

        body.TryGetComponent(out heldCollider);
        RefreshIgnoredCollisions();
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

    [ServerRpc]
    void SoftPushDynamicServerRpc(NetworkObject target, Vector3 fromPoint)
    {
        if (target == null || !target.TryGetComponent(out Rigidbody body) || body.isKinematic)
            return;

        PlayerSoftPush.SoftPushDynamicBody(body, fromPoint, softPushAcceleration, maxSoftPushSpeed);
    }

    void ForceDrop(Vector3 impulse)
    {
        ClearIgnoredCollisions();

        if (heldObject != null && heldObject.TryGetComponent(out Rigidbody body))
        {
            bool revivedPlayer = heldObject.TryGetComponent(out PlayerHealth heldHealth) && !heldHealth.IsDead;
            if (revivedPlayer)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
            else
            {
                body.isKinematic = wasKinematic;
                body.useGravity = usedGravity;

                if (!body.isKinematic && impulse.sqrMagnitude > 0f)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.AddForce(impulse, ForceMode.VelocityChange);
                }
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
        if (IsOwner && !isHolding.Value && ignoredColliders.Count > 0)
            ClearIgnoredCollisions();

        if (!IsOwner || !isHolding.Value)
            return;

        if (pauseMenu != null && pauseMenu.IsOpen)
            return;

        NetworkObject held = heldSync.Value;
        if (held != null && held.TryGetComponent(out PlayerHealth heldHealth) && !heldHealth.IsDead)
        {
            DropServerRpc(Vector3.zero);
            return;
        }

        if (heldCollider == null && held != null)
            held.TryGetComponent(out heldCollider);

        if (Time.time >= nextIgnoreRefreshTime)
            RefreshIgnoredCollisions();

        if (!TryGetHoldPose(out Vector3 desired, out Quaternion rot))
            return;

        if (!TryGetHeldRoot(out Transform heldRoot))
            return;

        if (heldCollider == null)
            heldRoot.TryGetComponent(out heldCollider);

        Vector3 pos = desired;
        if (heldCollider != null)
        {
            pos = HeldObjectHoldQuery.ResolvePose(
                heldCollider,
                heldRoot,
                transform,
                desired,
                rot,
                out Vector3 correction);

            if (controller != null && correction.sqrMagnitude > 0.0001f)
            {
                Vector3 planar = new Vector3(correction.x, 0f, correction.z);
                if (planar.sqrMagnitude > 0.0001f)
                    controller.Move(planar);
            }
        }

        UpdateHoldServerRpc(pos, rot);
        ApplyHoldLocally(pos, rot);

        float radius = HeldObjectHoldQuery.EstimateRadius(heldCollider);
        SoftPushDynamicsAround(pos, radius);
        SoftPushOtherPlayers(pos, radius);
    }

    void SoftPushOtherPlayers(Vector3 holdPos, float objectRadius)
    {
        int count = Physics.OverlapSphereNonAlloc(
            holdPos,
            objectRadius + 0.08f,
            softPushBuffer,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float step = 4f * Time.deltaTime;
        for (int i = 0; i < count; i++)
        {
            Collider col = softPushBuffer[i];
            if (col == null)
                continue;

            CharacterController other = col as CharacterController;
            if (other == null)
                other = col.GetComponentInParent<CharacterController>();
            if (other == null || other == controller)
                continue;

            Vector3 away = other.transform.position - holdPos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = transform.forward;
            away.Normalize();
            other.Move(away * step);
        }
    }

    void SoftPushDynamicsAround(Vector3 holdPos, float radius)
    {
        int count = Physics.OverlapSphereNonAlloc(
            holdPos,
            radius + 0.05f,
            softPushBuffer,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider col = softPushBuffer[i];
            if (col == null || col == heldCollider)
                continue;

            Rigidbody rb = col.attachedRigidbody;
            if (rb == null || rb.isKinematic)
                continue;
            if (rb.GetComponentInParent<CharacterController>() != null)
                continue;
            if (!rb.TryGetComponent(out NetworkObject nob) || !nob.IsSpawned)
                continue;

            SoftPushDynamicServerRpc(nob, holdPos);
        }
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

    void RefreshIgnoredCollisions()
    {
        nextIgnoreRefreshTime = Time.time + 0.5f;
        if (heldCollider == null && heldSync.Value != null)
            heldSync.Value.TryGetComponent(out heldCollider);
        if (heldCollider == null && heldObject != null)
            heldObject.TryGetComponent(out heldCollider);
        if (heldCollider == null)
            return;

        ClearIgnoredCollisions();

        if (controller != null)
        {
            Physics.IgnoreCollision(controller, heldCollider, true);
            ignoredColliders.Add(controller);
        }

        CharacterController[] controllers = Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            CharacterController cc = controllers[i];
            if (cc == null || cc == controller)
                continue;
            Physics.IgnoreCollision(cc, heldCollider, true);
            ignoredColliders.Add(cc);
        }

        Rigidbody heldRb = null;
        if (heldObject != null)
            heldObject.TryGetComponent(out heldRb);
        else if (heldSync.Value != null)
            heldSync.Value.TryGetComponent(out heldRb);

        Rigidbody[] bodies = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody rb = bodies[i];
            if (rb == null || rb == heldRb || rb.isKinematic)
                continue;
            if (rb.GetComponentInParent<CharacterController>() != null)
                continue;

            Collider[] cols = rb.GetComponentsInChildren<Collider>();
            for (int c = 0; c < cols.Length; c++)
            {
                if (cols[c] == null || cols[c].isTrigger)
                    continue;
                Physics.IgnoreCollision(heldCollider, cols[c], true);
                ignoredColliders.Add(cols[c]);
            }
        }
    }

    void ClearIgnoredCollisions()
    {
        if (heldCollider != null)
        {
            for (int i = 0; i < ignoredColliders.Count; i++)
            {
                Collider col = ignoredColliders[i];
                if (col != null)
                    Physics.IgnoreCollision(heldCollider, col, false);
            }
        }

        ignoredColliders.Clear();
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
        ClearIgnoredCollisions();
    }
}
