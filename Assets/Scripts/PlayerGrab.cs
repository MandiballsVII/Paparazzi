using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Agarre compartido (local, Gym y online).
/// Online: NetworkPlayerGrab engancha handlers y SyncVars; este script hace hold/look/soft-push.
/// </summary>
[RequireComponent(typeof(PlayerInput))]
[DisallowMultipleComponent]
public class PlayerGrab : MonoBehaviour
{
    [SerializeField] float grabDistance = 3f;
    [SerializeField] float holdDistance = 1.8f;
    [SerializeField] float moveThrowThreshold = 0.75f;
    [SerializeField] float throwSpeedFactor = 2.5f;
    [SerializeField] float minThrowForce = 4f;
    [SerializeField] float maxThrowForce = 18f;
    [SerializeField] float maxGrabMass = 25f;

    [Header("Hold Collision")]
    [SerializeField] int lookSearchIterations = 10;
    [SerializeField] float softPushAcceleration = 16f;
    [SerializeField] float maxSoftPushSpeed = 3f;

    [SerializeField]
    [Tooltip("Intervalo de sync de pose cuando hay NetworkHoldSyncHandler.")]
    float holdNetworkSyncInterval = 0.05f;

    PlayerInput playerInput;
    PlayerPauseMenu pauseMenu;
    PlayerHealth health;
    Camera playerCamera;
    CharacterController controller;
    InputAction grabAction;
    bool inputBound;

    Rigidbody heldBody;
    Collider heldCollider;
    bool wasKinematic;
    bool usedGravity;
    bool physicsConfiguredByUs;
    readonly List<Collider> ignoredColliders = new();
    static readonly Collider[] otherPlayerBuffer = new Collider[16];
    float nextIgnoreRefreshTime;
    float nextHoldNetworkSyncTime;
    bool grabActive = true;

    public float GrabDistance => grabDistance;
    public float MaxGrabMass => maxGrabMass;
    public float SoftPushAcceleration => softPushAcceleration;
    public float MaxSoftPushSpeed => maxSoftPushSpeed;
    public bool IsHolding => heldBody != null;

    public bool GrabActive
    {
        get => grabActive;
        set
        {
            grabActive = value;
            if (grabActive && isActiveAndEnabled)
                BindInput();
            else
                UnbindInput();
        }
    }

    /// <summary>Red: raycast ya válido; devolver true si la red consume el grab.</summary>
    public Func<RaycastHit, bool> NetworkGrabHandler { get; set; }

    /// <summary>Red: soltar vía ServerRpc en lugar de Drop local.</summary>
    public Action<Vector3> NetworkDropHandler { get; set; }

    /// <summary>Red: sincronizar pose del hold al servidor.</summary>
    public Action<Vector3, Quaternion> NetworkHoldSyncHandler { get; set; }

    /// <summary>Red: soft-push de rigidbodies con NetworkObject.</summary>
    public Action<Rigidbody, Vector3> SoftPushDynamicHandler { get; set; }

    public void ClearNetworkHandlers()
    {
        NetworkGrabHandler = null;
        NetworkDropHandler = null;
        NetworkHoldSyncHandler = null;
        SoftPushDynamicHandler = null;
    }

    public void Initialize(Camera camera)
    {
        playerCamera = camera;
        if (grabActive && isActiveAndEnabled)
            BindInput();
    }

    /// <summary>Hold local: configura física. Hold online: solo tracking (física en servidor).</summary>
    public void AttachHold(Rigidbody body, Collider col, bool configurePhysics)
    {
        if (body == null)
            return;

        if (heldBody != null && heldBody != body)
            ReleaseHold(Vector3.zero, restorePhysics: physicsConfiguredByUs);

        heldBody = body;
        heldCollider = col != null ? col : body.GetComponent<Collider>();
        physicsConfiguredByUs = configurePhysics;

        if (configurePhysics)
        {
            wasKinematic = body.isKinematic;
            usedGravity = body.useGravity;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
        }

        RefreshIgnoredCollisions();
    }

    public void ReleaseHold(Vector3 impulse, bool restorePhysics)
    {
        ClearIgnoredCollisions();

        if (heldBody != null && restorePhysics)
        {
            bool revivedPlayer = heldBody.TryGetComponent(out PlayerHealth heldHealth) && !heldHealth.IsDead;
            if (revivedPlayer)
            {
                heldBody.isKinematic = true;
                heldBody.useGravity = false;
            }
            else
            {
                heldBody.isKinematic = wasKinematic;
                heldBody.useGravity = usedGravity;

                if (!heldBody.isKinematic && impulse.sqrMagnitude > 0f)
                {
                    heldBody.linearVelocity = Vector3.zero;
                    heldBody.angularVelocity = Vector3.zero;
                    heldBody.AddForce(impulse, ForceMode.VelocityChange);
                }
            }
        }

        heldBody = null;
        heldCollider = null;
        physicsConfiguredByUs = false;
    }

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

    void OnEnable()
    {
        if (grabActive)
            BindInput();
    }

    void OnDisable()
    {
        UnbindInput();
        ClearIgnoredCollisions();

        // Online: la red suelta en OnStopClient / SyncVar. Offline: soltar aquí.
        if (NetworkDropHandler == null && heldBody != null)
            ReleaseHold(Vector3.zero, restorePhysics: physicsConfiguredByUs);
        else if (heldBody != null && NetworkDropHandler != null)
        {
            heldBody = null;
            heldCollider = null;
            physicsConfiguredByUs = false;
        }
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

    bool IsMenuOpen()
    {
        if (pauseMenu == null)
            TryGetComponent(out pauseMenu);
        return pauseMenu != null && pauseMenu.IsOpen;
    }

    void OnGrabPressed(InputAction.CallbackContext context)
    {
        if (!grabActive || IsMenuOpen())
            return;

        if (health == null)
            TryGetComponent(out health);
        if (health != null && health.IsDead)
            return;

        if (heldBody == null
            && playerCamera != null
            && PlayerInteractUtility.TryGetInteractable(playerCamera, grabDistance, out IPlayerInteractable interactable, out _)
            && interactable.CanInteract(gameObject))
        {
            interactable.Interact(gameObject);
            return;
        }

        if (heldBody != null)
        {
            Vector3 impulse = CalculateReleaseImpulse();
            if (NetworkDropHandler != null)
                NetworkDropHandler.Invoke(impulse);
            else
                ReleaseHold(impulse, restorePhysics: true);
            return;
        }

        TryGrab();
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
        if (heldBody == null || heldCollider == null || playerCamera == null || body == null || cameraPivot == null)
            return;

        if (Mathf.Abs(Mathf.DeltaAngle(previousYaw, yaw)) < 0.0001f
            && Mathf.Abs(pitch - previousPitch) < 0.0001f)
            return;

        ApplyLook(body, cameraPivot, yaw, pitch);
        if (!IsCurrentHoldBlocked())
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

            if (IsCurrentHoldBlocked())
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

    bool IsCurrentHoldBlocked()
    {
        if (heldBody == null || heldCollider == null || playerCamera == null)
            return false;

        Transform cam = playerCamera.transform;
        Vector3 holdPoint = cam.position + cam.forward * holdDistance;
        return HeldObjectHoldQuery.IsPoseBlocked(
            heldCollider,
            heldBody.transform,
            transform,
            holdPoint,
            cam.rotation);
    }

    void LateUpdate()
    {
        if (!grabActive || IsMenuOpen())
            return;

        if (heldBody == null || playerCamera == null)
            return;

        if (heldBody.TryGetComponent(out PlayerHealth heldHealth) && !heldHealth.IsDead)
        {
            if (NetworkDropHandler != null)
                NetworkDropHandler.Invoke(Vector3.zero);
            else
                ReleaseHold(Vector3.zero, restorePhysics: true);
            return;
        }

        if (heldCollider == null)
            heldBody.TryGetComponent(out heldCollider);

        if (Time.time >= nextIgnoreRefreshTime)
            RefreshIgnoredCollisions();

        Transform cam = playerCamera.transform;
        Vector3 desired = cam.position + cam.forward * holdDistance;
        Quaternion rot = cam.rotation;

        Vector3 holdPoint = HeldObjectHoldQuery.ResolvePose(
            heldCollider,
            heldBody.transform,
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

        ApplyHoldPose(heldBody, holdPoint, rot);

        if (NetworkHoldSyncHandler != null && Time.time >= nextHoldNetworkSyncTime)
        {
            nextHoldNetworkSyncTime = Time.time + holdNetworkSyncInterval;
            NetworkHoldSyncHandler.Invoke(holdPoint, rot);
        }

        float radius = HeldObjectHoldQuery.EstimateRadius(heldCollider);
        SoftPushDynamicsAround(holdPoint, radius);
        SoftPushOtherPlayers(holdPoint, radius);
    }

    static void ApplyHoldPose(Rigidbody body, Vector3 position, Quaternion rotation)
    {
        if (body == null)
            return;
        body.MovePosition(position);
        body.MoveRotation(rotation);
        body.transform.SetPositionAndRotation(position, rotation);
    }

    void SoftPushDynamicsAround(Vector3 holdPos, float radius)
    {
        if (SoftPushDynamicHandler != null)
        {
            int count = Physics.OverlapSphereNonAlloc(
                holdPos,
                radius + 0.05f,
                otherPlayerBuffer,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider col = otherPlayerBuffer[i];
                if (col == null || col == heldCollider)
                    continue;

                Rigidbody rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic || rb == heldBody)
                    continue;
                if (rb.GetComponentInParent<CharacterController>() != null)
                    continue;

                SoftPushDynamicHandler.Invoke(rb, holdPos);
            }

            return;
        }

        PlayerSoftPush.SoftPushDynamicsAround(
            holdPos,
            radius + 0.05f,
            softPushAcceleration,
            maxSoftPushSpeed,
            heldBody);
    }

    void SoftPushOtherPlayers(Vector3 holdPos, float objectRadius)
    {
        int count = Physics.OverlapSphereNonAlloc(
            holdPos,
            objectRadius + 0.08f,
            otherPlayerBuffer,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float step = 4f * Time.deltaTime;
        for (int i = 0; i < count; i++)
        {
            Collider col = otherPlayerBuffer[i];
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

    void TryGrab()
    {
        if (playerCamera == null)
            return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, grabDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        Rigidbody body = hit.rigidbody;
        if (body == null)
            return;

        bool isDeadPlayer = body.TryGetComponent(out PlayerHealth targetHealth) && targetHealth.IsDead;
        if (!isDeadPlayer && body.mass > maxGrabMass)
            return;
        if (isDeadPlayer && body.gameObject == gameObject)
            return;

        if (NetworkGrabHandler != null)
        {
            NetworkGrabHandler.Invoke(hit);
            return;
        }

        AttachHold(body, hit.collider, configurePhysics: true);
    }

    void RefreshIgnoredCollisions()
    {
        nextIgnoreRefreshTime = Time.time + 0.5f;
        if (heldCollider == null)
            return;

        ClearIgnoredCollisions();

        if (controller != null)
        {
            Physics.IgnoreCollision(controller, heldCollider, true);
            ignoredColliders.Add(controller);
        }

        CharacterController[] controllers = FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            CharacterController cc = controllers[i];
            if (cc == null || cc == controller)
                continue;
            Physics.IgnoreCollision(cc, heldCollider, true);
            ignoredColliders.Add(cc);
        }

        Rigidbody[] bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody rb = bodies[i];
            if (rb == null || rb == heldBody || rb.isKinematic)
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
        if (!grabActive || playerCamera == null)
            return;

        Rect pixel = playerCamera.pixelRect;
        float x = pixel.x + pixel.width * 0.5f;
        float y = Screen.height - (pixel.y + pixel.height * 0.5f);
        GUI.color = heldBody != null ? Color.yellow : Color.white;
        GUI.Box(new Rect(x - 3f, y - 3f, 6f, 6f), GUIContent.none);
    }
}
