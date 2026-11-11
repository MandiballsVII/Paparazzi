using System;
using UnityEngine;

/// <summary>
/// Junta visión/oído y guarda conocimiento del mundo (última posición vista/oída).
/// Pensado para alimentar Behavior Trees más adelante.
/// </summary>
[DisallowMultipleComponent]
public class PerceptionAgent : MonoBehaviour
{
    [SerializeField]
    bool hasVision = true;

    [SerializeField]
    bool hasHearing = true;

    [SerializeField]
    bool visionLogDetections;
    [SerializeField]
    bool hearingLogDetections;

    [SerializeField]
    float knowledgeStaleSeconds = 8f;

    VisionSensor vision;
    HearingSensor hearing;

    public event Action<PerceptionTarget, Vector3> SawTarget;
    public event Action<NoiseEventData, float, int> HeardNoise;

    public bool HasVision => hasVision;
    public bool HasHearing => hasHearing;

    public bool VisionLogDetections
    {
        get => visionLogDetections;
        set => visionLogDetections = value;
    }
    public bool HearingLogDetections
    {
        get => hearingLogDetections;
        set => hearingLogDetections = value;
    }

    public bool CurrentlySeesTarget { get; private set; }
    public PerceptionTarget LastSeenTarget { get; private set; }
    public Vector3 LastSeenPosition { get; private set; }
    public float LastSeenTime { get; private set; } = -999f;

    public Vector3 LastHeardPosition { get; private set; }
    public float LastHeardLoudness { get; private set; }
    public NoiseType LastHeardType { get; private set; }
    public float LastHeardTime { get; private set; } = -999f;
    public Transform LastHeardInstigator { get; private set; }

    public float TimeSinceSeen => Time.time - LastSeenTime;
    public float TimeSinceHeard => Time.time - LastHeardTime;
    public bool HasFreshSight => CurrentlySeesTarget || TimeSinceSeen <= knowledgeStaleSeconds;
    public bool HasFreshHearing => TimeSinceHeard <= knowledgeStaleSeconds;

    void Awake()
    {
        EnsureSensors();
        ApplySenseToggles();
    }

    void OnEnable()
    {
        EnsureSensors();
        ApplySenseToggles();
    }

    void EnsureSensors()
    {
        if (!TryGetComponent(out vision))
            vision = gameObject.AddComponent<VisionSensor>();
        if (!TryGetComponent(out hearing))
            hearing = gameObject.AddComponent<HearingSensor>();

        vision.Bind(this);
        hearing.Bind(this);
    }

    void ApplySenseToggles()
    {
        if (vision != null)
            vision.VisionEnabled = hasVision;
        if (hearing != null)
            hearing.HearingEnabled = hasHearing;
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
            return;
        ApplySenseToggles();
    }

    public void NotifySaw(PerceptionTarget target, Vector3 aimPosition)
    {
        if (!hasVision || target == null)
            return;

        CurrentlySeesTarget = true;
        LastSeenTarget = target;
        LastSeenPosition = aimPosition;
        LastSeenTime = Time.time;

        SawTarget?.Invoke(target, aimPosition);

        if (visionLogDetections)
            Debug.Log($"[{name}] VE a {target.name} @ {aimPosition}", this);
    }

    public void NotifyHeard(in NoiseEventData noise, float effectiveLoudness, int wallsCrossed)
    {
        if (!hasHearing)
            return;

        LastHeardPosition = noise.Position;
        LastHeardLoudness = effectiveLoudness;
        LastHeardType = noise.Type;
        LastHeardTime = Time.time;
        LastHeardInstigator = noise.Instigator;

        HeardNoise?.Invoke(noise, effectiveLoudness, wallsCrossed);

        if (hearingLogDetections)
        {
            Debug.Log(
                $"[{name}] OYE {noise.Type} loud={effectiveLoudness:F2} walls={wallsCrossed} @ {noise.Position}",
                this);
        }
    }

    /// <summary>
    /// Llamar al final del scan de visión si ningún target fue visto este ciclo.
    /// </summary>
    public void ClearCurrentSight()
    {
        CurrentlySeesTarget = false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (HasFreshSight)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(LastSeenPosition, 0.25f);
            Gizmos.DrawLine(transform.position + Vector3.up, LastSeenPosition);
        }

        if (HasFreshHearing)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(LastHeardPosition, 0.2f);
            Gizmos.DrawLine(transform.position + Vector3.up, LastHeardPosition);
        }
    }
#endif
}
