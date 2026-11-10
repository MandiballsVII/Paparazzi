using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
public class PlayerGrab : MonoBehaviour
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

    PlayerInput playerInput;
    PlayerPauseMenu pauseMenu;
    PlayerHealth health;
    Camera playerCamera;
    CharacterController controller;
    InputAction grabAction;
    Rigidbody heldBody;
    Collider heldCollider;
    bool wasKinematic;
    bool usedGravity;
    readonly List<Collider> ignoredColliders = new();
    static readonly Collider[] otherPlayerBuffer = new Collider[16];
    float nextIgnoreRefreshTime;

    public bool IsHolding => heldBody != null;

    public void Initialize(Camera camera)
    {
        playerCamera = camera;
    }

    void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        TryGetComponent(out controller);
        TryGetComponent(out pauseMenu);
        TryGetComponent(out health);
    }

    void OnEnable()
    {
        grabAction = playerInput.actions["Interact"];
        grabAction.performed += OnGrabPressed;
    }

    void OnDisable()
    {
        if (grabAction != null)
            grabAction.performed -= OnGrabPressed;

        Drop(Vector3.zero);
    }

    bool IsMenuOpen()
    {
        if (pauseMenu == null)
            TryGetComponent(out pauseMenu);
        return pauseMenu != null && pauseMenu.IsOpen;
    }

    void OnGrabPressed(InputAction.CallbackContext context)
    {
        if (IsMenuOpen())
            return;

        if (health == null)
            TryGetComponent(out health);
        if (health != null && health.IsDead)
            return;

        if (heldBody != null)
            Drop(CalculateReleaseImpulse());
        else
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

    /// <summary>
    /// Tope de cámara: si la pose del collider del objeto penetraría, no deja mirar más en esa dirección.
    /// El portador NUNCA se empuja.
    /// </summary>
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
        if (IsMenuOpen())
            return;

        if (heldBody == null || playerCamera == null)
            return;

        if (heldBody.TryGetComponent(out PlayerHealth heldHealth) && !heldHealth.IsDead)
        {
            Drop(Vector3.zero);
            return;
        }

        if (Time.time >= nextIgnoreRefreshTime)
            RefreshIgnoredCollisions();

        Transform cam = playerCamera.transform;
        Vector3 desired = cam.position + cam.forward * holdDistance;
        Quaternion rot = cam.rotation;

        // Al andar: el collider real no atraviesa; si hace falta, frena al player solo con esa corrección.
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

        heldBody.MovePosition(holdPoint);
        heldBody.MoveRotation(rot);
        heldBody.transform.SetPositionAndRotation(holdPoint, rot);

        float radius = HeldObjectHoldQuery.EstimateRadius(heldCollider);
        PlayerSoftPush.SoftPushDynamicsAround(
            holdPoint,
            radius + 0.05f,
            softPushAcceleration,
            maxSoftPushSpeed,
            heldBody);

        SoftPushOtherPlayers(holdPoint, radius);
    }

    void SoftPushOtherPlayers(Vector3 holdPos, float objectRadius)
    {
        // Empuja al OTRO jugador, nunca al portador.
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

        heldBody = body;
        heldCollider = hit.collider;
        wasKinematic = body.isKinematic;
        usedGravity = body.useGravity;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;

        RefreshIgnoredCollisions();
    }

    void Drop(Vector3 impulse)
    {
        ClearIgnoredCollisions();

        if (heldBody != null)
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
    }

    void RefreshIgnoredCollisions()
    {
        nextIgnoreRefreshTime = Time.time + 0.5f;
        if (heldCollider == null)
            return;

        ClearIgnoredCollisions();

        // Sin colisión física dura: el tope es el look + ComputePenetration.
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

        Rigidbody[] bodies = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
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
        if (playerCamera == null)
            return;

        Rect pixel = playerCamera.pixelRect;
        float x = pixel.x + pixel.width * 0.5f;
        float y = Screen.height - (pixel.y + pixel.height * 0.5f);
        GUI.color = heldBody != null ? Color.yellow : Color.white;
        GUI.Box(new Rect(x - 3f, y - 3f, 6f, 6f), GUIContent.none);
    }
}
