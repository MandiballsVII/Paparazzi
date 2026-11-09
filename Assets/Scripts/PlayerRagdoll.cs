using UnityEngine;

[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(CharacterController))]
public class PlayerRagdoll : MonoBehaviour
{
    [SerializeField] float corpseMass = 18f;

    PlayerHealth health;
    CharacterController characterController;
    Rigidbody body;
    CapsuleCollider ragdollCollider;
    PlayerStance stance;
    bool ragdollActive;

    public bool IsRagdollActive => ragdollActive;
    public Rigidbody Body => body;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        characterController = GetComponent<CharacterController>();
        TryGetComponent(out stance);
        EnsurePhysicsComponents();
    }

    void OnEnable()
    {
        health.Died += EnterRagdoll;
        health.Revived += ExitRagdoll;
    }

    void OnDisable()
    {
        health.Died -= EnterRagdoll;
        health.Revived -= ExitRagdoll;
    }

    void Start()
    {
        if (health.IsDead)
            EnterRagdoll();
        else
            ExitRagdoll();
    }

    void EnsurePhysicsComponents()
    {
        if (!TryGetComponent(out body))
            body = gameObject.AddComponent<Rigidbody>();

        body.mass = corpseMass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        body.isKinematic = true;
        body.useGravity = false;

        if (!TryGetComponent(out ragdollCollider))
            ragdollCollider = gameObject.AddComponent<CapsuleCollider>();

        // Coincide con cápsula placeholder (alto 2, radio 0.5).
        ragdollCollider.height = 2f;
        ragdollCollider.radius = 0.5f;
        ragdollCollider.center = Vector3.zero;
        ragdollCollider.enabled = false;
    }

    void EnterRagdoll()
    {
        if (ragdollActive)
            return;

        ragdollActive = true;

        if (characterController != null)
            characterController.enabled = false;

        if (stance != null)
            stance.SetCrouching(false, ignoreCeiling: true);

        SyncColliderToStance();

        if (ragdollCollider != null)
            ragdollCollider.enabled = true;

        if (body != null)
        {
            body.isKinematic = false;
            body.useGravity = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    void ExitRagdoll()
    {
        if (!ragdollActive && characterController != null && characterController.enabled)
            return;

        ragdollActive = false;

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
        }

        if (ragdollCollider != null)
            ragdollCollider.enabled = false;

        // Reorientar erguido al revivir.
        Vector3 euler = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(0f, euler.y, 0f);

        if (characterController != null)
            characterController.enabled = true;
    }

    void SyncColliderToStance()
    {
        if (ragdollCollider == null || characterController == null)
            return;

        ragdollCollider.height = characterController.height;
        ragdollCollider.radius = characterController.radius;
        ragdollCollider.center = characterController.center;
    }
}
