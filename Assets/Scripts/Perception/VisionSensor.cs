using System.Collections;
using UnityEngine;

/// <summary>
/// Visión configurable: FOV + distancia + line of sight (paredes bloquean).
/// </summary>
[DisallowMultipleComponent]
public class VisionSensor : MonoBehaviour
{
    [SerializeField]
    bool visionEnabled = true;

    [SerializeField]
    float viewDistance = 18f;

    [SerializeField]
    [Range(1f, 180f)]
    float fieldOfView = 90f;

    [SerializeField]
    float eyeHeight = 2.4f;

    [SerializeField]
    float scanInterval = 0.15f;

    [SerializeField]
    LayerMask occlusionMask = ~0;

    PerceptionAgent agent;
    Coroutine scanRoutine;

    public bool VisionEnabled
    {
        get => visionEnabled;
        set => visionEnabled = value;
    }

    public float ViewDistance => viewDistance;
    public float FieldOfView => fieldOfView;

    public void Bind(PerceptionAgent owner) => agent = owner;

    void OnEnable()
    {
        if (scanRoutine != null)
            StopCoroutine(scanRoutine);
        scanRoutine = StartCoroutine(ScanLoop());
    }

    void OnDisable()
    {
        if (scanRoutine != null)
        {
            StopCoroutine(scanRoutine);
            scanRoutine = null;
        }
    }

    IEnumerator ScanLoop()
    {
        var wait = new WaitForSeconds(scanInterval);
        while (enabled)
        {
            if (visionEnabled)
                ScanOnce();
            yield return wait;
        }
    }

    void ScanOnce()
    {
        if (agent == null && !TryGetComponent(out agent))
            return;

        agent.ClearCurrentSight();

        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 forward = transform.forward;
        float halfFov = fieldOfView * 0.5f;
        float maxSqr = viewDistance * viewDistance;

        var targets = PerceptionTarget.ActiveTargets;
        for (int i = 0; i < targets.Count; i++)
        {
            PerceptionTarget target = targets[i];
            if (target == null || !target.isActiveAndEnabled)
                continue;
            if (target.transform == transform || target.transform.IsChildOf(transform))
                continue;

            Vector3 aim = target.AimPosition;
            Vector3 toTarget = aim - eye;
            float sqr = toTarget.sqrMagnitude;
            if (sqr > maxSqr || sqr < 0.0001f)
                continue;

            float distance = Mathf.Sqrt(sqr);
            Vector3 direction = toTarget / distance;
            if (Vector3.Angle(forward, direction) > halfFov)
                continue;

            if (Physics.Raycast(
                    eye,
                    direction,
                    out RaycastHit hit,
                    distance,
                    occlusionMask,
                    QueryTriggerInteraction.Ignore))
            {
                Transform hitT = hit.transform;
                if (hitT != target.transform && !hitT.IsChildOf(target.transform))
                    continue;
            }

            agent.NotifySaw(target, aim);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!visionEnabled)
            return;

        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(eye, viewDistance);

        Vector3 forward = Application.isPlaying ? transform.forward : transform.forward;
        Quaternion left = Quaternion.Euler(0f, -fieldOfView * 0.5f, 0f);
        Quaternion right = Quaternion.Euler(0f, fieldOfView * 0.5f, 0f);
        Gizmos.DrawLine(eye, eye + left * forward * viewDistance);
        Gizmos.DrawLine(eye, eye + right * forward * viewDistance);
    }
#endif
}
