using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Patrulla aleatoria sobre NavMesh.
/// Choca con paredes/props/jugadores → cambia de dirección.
/// Props dinámicos: los empuja al impactar.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class CelebrityPatrol : MonoBehaviour
{
    [SerializeField]
    float roamRadius = 25f;

    [SerializeField]
    float waitAtPointMin = 0.5f;

    [SerializeField]
    float waitAtPointMax = 2.5f;

    [SerializeField]
    int sampleAttempts = 24;

    [SerializeField]
    bool roamFromSpawn = true;

    [SerializeField]
    float obstacleProbePadding = 0.45f;

    [SerializeField]
    float repathCooldown = 0.35f;

    [SerializeField]
    float propPushAcceleration = 22f;

    [SerializeField]
    float propMaxPushSpeed = 4f;

    [SerializeField]
    LayerMask obstacleMask = ~0;

    static readonly Collider[] OverlapBuffer = new Collider[24];

    NavMeshAgent agent;
    Vector3 roamOrigin;
    Coroutine patrolRoutine;
    float nextRepathAllowedTime;
    bool bumpRequested;
    Vector3 bumpNormal = Vector3.forward;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void OnEnable()
    {
        roamOrigin = transform.position;
        bumpRequested = false;
        if (patrolRoutine != null)
            StopCoroutine(patrolRoutine);
        patrolRoutine = StartCoroutine(PatrolLoop());
    }

    void OnDisable()
    {
        if (patrolRoutine != null)
        {
            StopCoroutine(patrolRoutine);
            patrolRoutine = null;
        }
    }

    IEnumerator PatrolLoop()
    {
        while (agent != null && !agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit snap, 5f, NavMesh.AllAreas))
            {
                agent.Warp(snap.position);
                break;
            }

            yield return null;
        }

        while (enabled && agent != null && agent.isOnNavMesh)
        {
            Vector3 preferAway = bumpRequested ? bumpNormal : Vector3.zero;
            bumpRequested = false;

            if (TryPickDestination(preferAway, out Vector3 destination))
            {
                agent.ResetPath();
                agent.SetDestination(destination);

                while (agent.pathPending)
                    yield return null;

                while (agent.isOnNavMesh
                       && agent.pathStatus != NavMeshPathStatus.PathInvalid
                       && agent.remainingDistance > agent.stoppingDistance)
                {
                    if (Time.time >= nextRepathAllowedTime && TryResolveContacts(out Vector3 hitNormal))
                    {
                        RequestBump(hitNormal);
                        agent.ResetPath();
                        break;
                    }

                    yield return null;
                }
            }

            if (bumpRequested)
                continue;

            float wait = Random.Range(waitAtPointMin, waitAtPointMax);
            float end = Time.time + wait;
            while (Time.time < end)
            {
                if (Time.time >= nextRepathAllowedTime && TryResolveContacts(out Vector3 hitNormal))
                {
                    RequestBump(hitNormal);
                    break;
                }

                yield return null;
            }
        }
    }

    void RequestBump(Vector3 hitNormal)
    {
        bumpRequested = true;
        bumpNormal = hitNormal;
        nextRepathAllowedTime = Time.time + repathCooldown;
    }

    /// <summary>
    /// Overlap (props/jugadores ya encima) + SphereCast (paredes/objetos delante).
    /// Empuja rigidbodies dinámicos.
    /// </summary>
    bool TryResolveContacts(out Vector3 hitNormal)
    {
        hitNormal = Vector3.forward;
        bool hitSomething = false;
        Vector3 accumNormal = Vector3.zero;

        float radius = Mathf.Max(0.15f, agent.radius * 0.95f);
        Vector3 origin = transform.position + Vector3.up * (agent.height * 0.5f);

        int count = Physics.OverlapSphereNonAlloc(
            origin,
            radius + 0.12f,
            OverlapBuffer,
            obstacleMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider col = OverlapBuffer[i];
            if (col == null)
                continue;
            if (col.transform == transform || col.transform.IsChildOf(transform))
                continue;

            // Jugadores
            CharacterController otherCc = col as CharacterController;
            if (otherCc == null)
                otherCc = col.GetComponentInParent<CharacterController>();
            if (otherCc != null)
            {
                Vector3 away = transform.position - otherCc.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f)
                    away = -transform.forward;
                away.Normalize();
                accumNormal += away;
                hitSomething = true;
                continue;
            }

            // Props levantables / dinámicos
            Rigidbody rb = col.attachedRigidbody;
            if (rb != null)
            {
                Vector3 away = rb.worldCenterOfMass - origin;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f)
                    away = transform.forward;
                away.Normalize();

                if (!rb.isKinematic)
                    PlayerSoftPush.SoftPushDynamicBody(rb, origin, propPushAcceleration, propMaxPushSpeed);

                // Normal para repath: alejarse del prop (opuesto a away desde el famoso).
                accumNormal += -away;
                hitSomething = true;
            }
        }

        // Paredes / estáticos delante (y props aún no solapados).
        Vector3 velocity = agent.velocity;
        if (velocity.sqrMagnitude < 0.05f)
            velocity = agent.desiredVelocity;

        if (velocity.sqrMagnitude >= 0.05f)
        {
            Vector3 direction = velocity;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                direction.Normalize();
                float probe = radius + obstacleProbePadding;

                if (Physics.SphereCast(
                        origin,
                        radius,
                        direction,
                        out RaycastHit hit,
                        probe,
                        obstacleMask,
                        QueryTriggerInteraction.Ignore)
                    && hit.transform != null
                    && hit.transform != transform
                    && !hit.transform.IsChildOf(transform))
                {
                    Rigidbody hitBody = hit.rigidbody;
                    if (hitBody != null && !hitBody.isKinematic)
                        PlayerSoftPush.SoftPushDynamicBody(hitBody, origin, propPushAcceleration, propMaxPushSpeed);

                    Vector3 n = hit.normal;
                    n.y = 0f;
                    if (n.sqrMagnitude < 0.0001f)
                        n = -direction;
                    else
                        n.Normalize();

                    accumNormal += n;
                    hitSomething = true;
                }
            }
        }

        if (!hitSomething)
            return false;

        hitNormal = accumNormal.sqrMagnitude > 0.0001f ? accumNormal.normalized : -transform.forward;
        return true;
    }

    bool TryPickDestination(Vector3 preferAwayNormal, out Vector3 destination)
    {
        Vector3 center = roamFromSpawn ? roamOrigin : transform.position;
        bool hasPrefer = preferAwayNormal.sqrMagnitude > 0.0001f;

        for (int i = 0; i < sampleAttempts; i++)
        {
            Vector2 circle = Random.insideUnitCircle * roamRadius;
            Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, roamRadius, NavMesh.AllAreas))
                continue;

            Vector3 delta = hit.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < 1f)
                continue;

            if (hasPrefer && Vector3.Dot(delta.normalized, preferAwayNormal) < 0.15f)
                continue;

            destination = hit.position;
            return true;
        }

        for (int i = 0; i < sampleAttempts; i++)
        {
            Vector2 circle = Random.insideUnitCircle * roamRadius;
            Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, roamRadius, NavMesh.AllAreas))
                continue;
            if ((hit.position - transform.position).sqrMagnitude < 1f)
                continue;

            destination = hit.position;
            return true;
        }

        destination = transform.position;
        return false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying && roamFromSpawn ? roamOrigin : transform.position;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.25f);
        Gizmos.DrawWireSphere(center, roamRadius);
    }
#endif
}
