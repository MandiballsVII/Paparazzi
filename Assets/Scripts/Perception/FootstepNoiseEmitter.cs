using UnityEngine;

/// <summary>
/// Emite ruido de pasos / aterrizaje según velocidad y stance.
/// Crouch = 0, walk &lt; sprint. Funciona con CharacterController local o con delta de transform (remotos).
/// </summary>
[DisallowMultipleComponent]
public class FootstepNoiseEmitter : MonoBehaviour
{
    [SerializeField]
    float walkLoudness = 0.35f;

    [SerializeField]
    float sprintLoudness = 0.8f;

    [SerializeField]
    float walkStepInterval = 0.48f;

    [SerializeField]
    float sprintStepInterval = 0.32f;

    [SerializeField]
    float walkSpeedThreshold = 0.35f;

    [SerializeField]
    float sprintSpeedThreshold = 6.2f;

    [SerializeField]
    float landingMinVerticalSpeed = 3f;

    [SerializeField]
    float landingLoudnessPerSpeed = 0.12f;

    [SerializeField]
    float maxLandingLoudness = 1.2f;

    [SerializeField]
    float footHeight = 0.05f;

    CharacterController controller;
    PlayerStance stance;
    Vector3 lastPosition;
    float nextStepTime;
    bool wasGrounded = true;
    float peakAirDownSpeed;

    void Awake()
    {
        TryGetComponent(out controller);
        TryGetComponent(out stance);
        lastPosition = transform.position;
    }

    void OnEnable()
    {
        lastPosition = transform.position;
        nextStepTime = 0f;
        wasGrounded = IsGrounded();
        peakAirDownSpeed = 0f;
    }

    void Update()
    {
        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        lastPosition = position;

        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        float horizontalSpeed = new Vector3(delta.x, 0f, delta.z).magnitude / dt;
        bool grounded = IsGrounded();

        if (!grounded)
        {
            float down = -delta.y / dt;
            if (down > peakAirDownSpeed)
                peakAirDownSpeed = down;
            wasGrounded = false;
            return;
        }

        if (!wasGrounded)
        {
            EmitLanding(peakAirDownSpeed);
            peakAirDownSpeed = 0f;
        }

        wasGrounded = true;

        bool crouching = stance != null && stance.IsCrouching;
        if (crouching || horizontalSpeed < walkSpeedThreshold)
            return;

        bool sprinting = horizontalSpeed >= sprintSpeedThreshold;
        float loudness = sprinting ? sprintLoudness : walkLoudness;
        float interval = sprinting ? sprintStepInterval : walkStepInterval;

        if (Time.time < nextStepTime)
            return;

        nextStepTime = Time.time + interval;
        Vector3 footPos = position + Vector3.up * footHeight;
        NoiseHub.Emit(footPos, loudness, NoiseType.Footstep, transform);
    }

    void EmitLanding(float downSpeed)
    {
        if (downSpeed < landingMinVerticalSpeed)
            return;

        float loudness = Mathf.Min(maxLandingLoudness, downSpeed * landingLoudnessPerSpeed);
        if (stance != null && stance.IsCrouching)
            loudness *= 0.35f;

        NoiseHub.Emit(transform.position + Vector3.up * footHeight, loudness, NoiseType.Landing, transform);
    }

    bool IsGrounded()
    {
        if (controller != null && controller.enabled)
            return controller.isGrounded;

        // Remotos / sin CC: ray corto hacia abajo.
        return Physics.Raycast(
            transform.position + Vector3.up * 0.2f,
            Vector3.down,
            0.35f,
            ~0,
            QueryTriggerInteraction.Ignore);
    }
}
