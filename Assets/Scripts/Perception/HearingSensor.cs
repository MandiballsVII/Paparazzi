using UnityEngine;

/// <summary>
/// Oído configurable. El sonido atraviesa paredes con atenuación por muro.
/// </summary>
[DisallowMultipleComponent]
public class HearingSensor : MonoBehaviour
{
    static readonly RaycastHit[] OcclusionHits = new RaycastHit[16];

    [SerializeField]
    bool hearingEnabled = true;

    [SerializeField]
    float sensitivity = 1f;

    [SerializeField]
    [Tooltip("Metros de alcance por unidad de loudness (tras sensibilidad).")]
    float metersPerLoudness = 12f;

    [SerializeField]
    float minDetectableLoudness = 0.05f;

    [SerializeField]
    [Range(0.05f, 1f)]
    [Tooltip("Multiplicador de loudness restante por cada pared atravesada.")]
    float wallAttenuation = 0.55f;

    [SerializeField]
    LayerMask wallMask = ~0;

    [SerializeField]
    float earHeight = 1.6f;

    PerceptionAgent agent;

    public bool HearingEnabled
    {
        get => hearingEnabled;
        set => hearingEnabled = value;
    }

    public void Bind(PerceptionAgent owner) => agent = owner;

    void OnEnable()
    {
        NoiseHub.Emitted += OnNoiseEmitted;
    }

    void OnDisable()
    {
        NoiseHub.Emitted -= OnNoiseEmitted;
    }

    void OnNoiseEmitted(NoiseEventData noise)
    {
        if (!hearingEnabled || !isActiveAndEnabled)
            return;
        if (agent == null && !TryGetComponent(out agent))
            return;
        if (noise.Instigator == transform || (noise.Instigator != null && noise.Instigator.IsChildOf(transform)))
            return;

        if (!TryEvaluate(noise, out float effectiveLoudness, out int wallsCrossed))
            return;

        agent.NotifyHeard(noise, effectiveLoudness, wallsCrossed);
    }

    public bool TryEvaluate(in NoiseEventData noise, out float effectiveLoudness, out int wallsCrossed)
    {
        effectiveLoudness = 0f;
        wallsCrossed = 0;

        float loudness = noise.Loudness * Mathf.Max(0f, sensitivity);
        if (loudness < minDetectableLoudness)
            return false;

        Vector3 ear = transform.position + Vector3.up * earHeight;
        Vector3 toNoise = noise.Position - ear;
        float distance = toNoise.magnitude;
        if (distance < 0.01f)
        {
            effectiveLoudness = loudness;
            return true;
        }

        Vector3 direction = toNoise / distance;
        int hitCount = Physics.RaycastNonAlloc(
            ear,
            direction,
            OcclusionHits,
            distance,
            wallMask,
            QueryTriggerInteraction.Ignore);

        wallsCrossed = CountBlockingWalls(hitCount, noise.Instigator);
        for (int i = 0; i < wallsCrossed; i++)
            loudness *= wallAttenuation;

        if (loudness < minDetectableLoudness)
            return false;

        float reach = loudness * metersPerLoudness;
        if (distance > reach)
            return false;

        effectiveLoudness = loudness;
        return true;
    }

    int CountBlockingWalls(int hitCount, Transform instigator)
    {
        int walls = 0;
        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = OcclusionHits[i].transform;
            if (hitTransform == null)
                continue;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;
            if (instigator != null && (hitTransform == instigator || hitTransform.IsChildOf(instigator)))
                continue;

            // No contar jugadores ni suelos (normal hacia arriba).
            if (hitTransform.TryGetComponent(out PerceptionTarget _)
                || hitTransform.GetComponentInParent<PerceptionTarget>() != null)
                continue;

            if (Vector3.Dot(OcclusionHits[i].normal, Vector3.up) > 0.65f)
                continue;

            walls++;
        }

        return walls;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!hearingEnabled)
            return;

        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.2f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * earHeight, metersPerLoudness * sensitivity);
    }
#endif
}
