using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Cerebro del famoso (lógica equivalente al Behavior Tree).
/// Estados: Patrol, WalkToAction, PerformAction, ListenIdle, InvestigateSound, Flee, Hidden.
/// El grafo visual de Unity Behavior podrá envolver estos estados más adelante.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(PerceptionAgent))]
[DefaultExecutionOrder(-20)]
public class CelebrityBrain : MonoBehaviour
{
    public enum AiState : byte
    {
        Patrol = 0,
        WalkToAction = 1,
        PerformAction = 2,
        ListenIdle = 3,
        InvestigateSound = 4,
        Flee = 5,
        Hidden = 6
    }

    enum InterruptedActivity : byte
    {
        None = 0,
        Patrol = 1,
        WalkToAction = 2,
        PerformAction = 3
    }

    [Header("Acción polémica")]
    [SerializeField]
    [Tooltip("Para pruebas puedes bajar a 10–20s.")]
    float actionCooldownMin = 90f;

    [SerializeField]
    float actionCooldownMax = 180f;

    [SerializeField]
    float actionDurationMin = 5f;

    [SerializeField]
    float actionDurationMax = 10f;

    [Header("Oído")]
    [SerializeField]
    float listenIdleDuration = 8f;

    [SerializeField]
    float listenIgnoreNoiseSeconds = 2f;

    [Header("Huida / ocultación")]
    [SerializeField]
    float fleeSpeed = 14f;

    [SerializeField]
    float walkSpeed = 2.2f;

    [SerializeField]
    float hideDurationMin = 10f;

    [SerializeField]
    float hideDurationMax = 15f;

    [SerializeField]
    Vector3 hideTeleportOffset = new Vector3(0f, -50f, 0f);

    [SerializeField]
    int fleeSampleAttempts = 48;

    [SerializeField]
    float fleeSearchRadius = 80f;

    [SerializeField]
    [Tooltip("Mínimo de huida visible antes de ocultarse (evita desaparecer en 1 frame tras foto).")]
    float minFleeDuration = 2f;

    [Header("Patrulla")]
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

    [Header("Debug")]
    [SerializeField]
    bool logStates = true;

    NavMeshAgent agent;
    PerceptionAgent perception;
    CelebrityPatrol patrol;
    Renderer[] renderers;
    Collider[] colliders;

    AiState state = AiState.Patrol;
    Vector3 roamOrigin;
    Vector3 preHidePosition;
    float defaultAgentSpeed;

    float actionCooldownRemaining;
    float actionTimeRemaining;
    float plannedActionDuration;
    bool actionInProgress;

    InterruptedActivity resumeActivity = InterruptedActivity.None;
    float listenStartedTime;
    bool investigateArmed;
    Vector3 investigateTarget;
    bool hasInvestigateTarget;

    Coroutine brainRoutine;
    bool visualsHidden;
    bool fleeAndStayHidden;
    bool puppetMode;

    public AiState CurrentState => state;
    public bool IsPerformingControversialAction => state == AiState.PerformAction;
    public bool HasFledPermanently => fleeAndStayHidden;
    public float ActionCooldownRemaining => actionCooldownRemaining;
    public float ActionTimeRemaining => actionTimeRemaining;
    public bool IsPuppet => puppetMode;
    public bool IsAuthorityRunning => brainRoutine != null && !puppetMode;
    public string CurrentActionId =>
        CelebrityRoundState.Instance != null && CelebrityRoundState.Instance.HasSelectedAction
            ? CelebrityRoundState.Instance.SelectedAction.Id
            : string.Empty;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        perception = GetComponent<PerceptionAgent>();
        TryGetComponent(out patrol);
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        defaultAgentSpeed = agent.speed > 0.1f ? agent.speed : walkSpeed;
        if (walkSpeed <= 0.1f)
            walkSpeed = defaultAgentSpeed;

        // CelebrityPatrol deja de auto-gestionar cuando hay Brain.
        if (patrol != null)
            patrol.SetBrainControlled(true);
    }

    /* void Update()
    {
        Debug.Log($"[{name}] Update: {state}");
        Debug.Log($"[{name}] Update: {actionCooldownRemaining:F1}s");
        Debug.Log($"[{name}] Update: {actionTimeRemaining:F1}s");
    } */

    void OnEnable()
    {
        roamOrigin = transform.position;

        // Online: NetworkCelebrity decide authority vs puppet.
        if (TryGetComponent(out NetworkCelebrity _))
            return;

        BeginAuthorityBrain();
    }

    void OnDisable()
    {
        StopAuthorityBrain();
        SetVisualsHidden(false);
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = false;
        if (agent != null)
            agent.speed = defaultAgentSpeed;
    }

    /// <summary>IA local o server. Idempotente.</summary>
    public void BeginAuthorityBrain()
    {
        puppetMode = false;
        if (agent != null)
            agent.enabled = true;

        CelebrityRoundState.GetOrCreate().EnsureSelection();

        perception.SawTarget -= OnSawTarget;
        perception.HeardNoise -= OnHeardNoise;
        perception.SawTarget += OnSawTarget;
        perception.HeardNoise += OnHeardNoise;

        if (brainRoutine != null)
            return;

        ResetRoutineAfterSpawnOrReappear(initial: true);
        brainRoutine = StartCoroutine(BrainLoop());
    }

    public void StopAuthorityBrain()
    {
        perception.SawTarget -= OnSawTarget;
        perception.HeardNoise -= OnHeardNoise;

        if (brainRoutine != null)
        {
            StopCoroutine(brainRoutine);
            brainRoutine = null;
        }
    }

    /// <summary>Cliente online: no simula IA; refleja SyncVars + NetworkTransform.</summary>
    public void SetPuppetMode(bool enabled)
    {
        puppetMode = enabled;
        if (!enabled)
            return;

        StopAuthorityBrain();
        if (agent != null)
        {
            if (agent.enabled && agent.isOnNavMesh)
                agent.ResetPath();
            agent.enabled = false;
        }

        // Clientes no necesitan escanear: la IA decide en el server.
        if (TryGetComponent(out VisionSensor vision))
            vision.enabled = false;
        if (TryGetComponent(out HearingSensor hearing))
            hearing.enabled = false;
    }

    public void ApplyNetworkState(
        AiState aiState,
        float cooldown,
        float actionTime,
        bool hidden,
        bool permanentFlee)
    {
        state = aiState;
        actionCooldownRemaining = cooldown;
        actionTimeRemaining = actionTime;
        fleeAndStayHidden = permanentFlee;
        actionInProgress = aiState == AiState.PerformAction;

        if (hidden != visualsHidden)
            SetVisualsHidden(hidden);
    }

    void ResetRoutineAfterSpawnOrReappear(bool initial)
    {
        actionInProgress = false;
        actionTimeRemaining = 0f;
        resumeActivity = InterruptedActivity.None;
        hasInvestigateTarget = false;
        investigateArmed = false;
        RollActionCooldown();
        SetState(AiState.Patrol, initial ? "spawn" : "reappear");
        agent.speed = walkSpeed;
        if (agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.ResetPath();
        }
    }

    void RollActionCooldown()
    {
        actionCooldownRemaining = Random.Range(actionCooldownMin, actionCooldownMax);
        if (logStates)
            Debug.Log($"[{name}] Cooldown acción: {actionCooldownRemaining:F1}s", this);
    }

    IEnumerator BrainLoop()
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

        while (enabled)
        {
            switch (state)
            {
                case AiState.Patrol:
                    yield return PatrolTick();
                    break;
                case AiState.WalkToAction:
                    yield return WalkToActionTick();
                    break;
                case AiState.PerformAction:
                    yield return PerformActionTick();
                    break;
                case AiState.ListenIdle:
                    yield return ListenIdleTick();
                    break;
                case AiState.InvestigateSound:
                    yield return InvestigateTick();
                    break;
                case AiState.Flee:
                    yield return FleeTick();
                    break;
                case AiState.Hidden:
                    yield return HiddenTick();
                    break;
                default:
                    yield return null;
                    break;
            }
        }
    }

    IEnumerator PatrolTick()
    {
        TickActionCooldown();

        if (actionCooldownRemaining <= 0f)
        {
            BeginControversialAction();
            yield return null;
            yield break;
        }

        if (!TryPickPatrolDestination(Vector3.zero, out Vector3 destination))
        {
            yield return null;
            yield break;
        }

        agent.isStopped = false;
        agent.speed = walkSpeed;
        agent.SetDestination(destination);

        while (state == AiState.Patrol && agent.pathPending)
            yield return null;

        while (state == AiState.Patrol
               && agent.isOnNavMesh
               && agent.pathStatus != NavMeshPathStatus.PathInvalid
               && agent.remainingDistance > agent.stoppingDistance)
        {
            TickActionCooldown();
            if (actionCooldownRemaining <= 0f)
            {
                agent.ResetPath();
                BeginControversialAction();
                yield break;
            }

            if (patrol != null)
                patrol.TickContactsWhileMoving();

            yield return null;
        }

        if (state != AiState.Patrol)
            yield break;

        float wait = Random.Range(waitAtPointMin, waitAtPointMax);
        float end = Time.time + wait;
        while (state == AiState.Patrol && Time.time < end)
        {
            TickActionCooldown();
            if (actionCooldownRemaining <= 0f)
            {
                BeginControversialAction();
                yield break;
            }

            yield return null;
        }
    }

    void TickActionCooldown()
    {
        if (state != AiState.Patrol)
            return;
        actionCooldownRemaining = Mathf.Max(0f, actionCooldownRemaining - Time.deltaTime);
    }

    void BeginControversialAction()
    {
        CelebrityRoundState round = CelebrityRoundState.GetOrCreate();
        round.EnsureSelection();
        CelebrityActionDefinition action = round.SelectedAction;

        plannedActionDuration = Random.Range(actionDurationMin, actionDurationMax);
        actionTimeRemaining = plannedActionDuration;
        actionInProgress = true;

        if (action.RequiresLocation && round.HasLocationPoint)
        {
            SetState(AiState.WalkToAction, $"hacia {action.Id} @ {round.LocationRoomName}");
            agent.isStopped = false;
            agent.speed = walkSpeed;
            agent.SetDestination(round.LocationPoint);
        }
        else
        {
            if (action.RequiresLocation && !round.HasLocationPoint && logStates)
                Debug.LogWarning($"[{name}] {action.Id} sin habitación; se hace in-place.", this);

            StartPerformAction();
        }
    }

    IEnumerator WalkToActionTick()
    {
        CelebrityRoundState round = CelebrityRoundState.Instance;
        if (round == null || !round.HasLocationPoint)
        {
            StartPerformAction();
            yield break;
        }

        if (!agent.pathPending
            && agent.remainingDistance <= agent.stoppingDistance + 0.15f
            && !agent.hasPath)
        {
            // Asegurar destino.
            agent.SetDestination(round.LocationPoint);
        }

        while (state == AiState.WalkToAction && agent.pathPending)
            yield return null;

        while (state == AiState.WalkToAction && agent.isOnNavMesh)
        {
            if (!agent.pathPending
                && agent.pathStatus != NavMeshPathStatus.PathInvalid
                && agent.remainingDistance <= agent.stoppingDistance + 0.15f)
                break;

            if (patrol != null)
                patrol.TickContactsWhileMoving();
            yield return null;
        }

        if (state == AiState.WalkToAction)
            StartPerformAction();
    }

    void StartPerformAction()
    {
        agent.ResetPath();
        agent.isStopped = true;
        SetState(AiState.PerformAction, $"{CurrentActionId} ({actionTimeRemaining:F1}s)");
        if (logStates)
            Debug.Log($"[{name}] REALIZA acción polémica: {CurrentActionId} durante {actionTimeRemaining:F1}s", this);
    }

    IEnumerator PerformActionTick()
    {
        while (state == AiState.PerformAction && actionTimeRemaining > 0f)
        {
            actionTimeRemaining -= Time.deltaTime;
            yield return null;
        }

        if (state != AiState.PerformAction)
            yield break;

        if (logStates)
            Debug.Log($"[{name}] FIN acción: {CurrentActionId}", this);

        actionInProgress = false;
        actionTimeRemaining = 0f;
        RollActionCooldown();
        agent.isStopped = false;
        SetState(AiState.Patrol, "post-acción");
    }

    IEnumerator ListenIdleTick()
    {
        agent.ResetPath();
        agent.isStopped = true;

        while (state == AiState.ListenIdle)
        {
            float elapsed = Time.time - listenStartedTime;
            if (elapsed >= listenIdleDuration)
            {
                ResumeInterrupted("listen timeout");
                yield break;
            }

            if (investigateArmed && elapsed >= listenIgnoreNoiseSeconds && hasInvestigateTarget)
            {
                SetState(AiState.InvestigateSound, investigateTarget.ToString("F1"));
                agent.isStopped = false;
                agent.speed = walkSpeed;
                agent.SetDestination(investigateTarget);
                investigateArmed = false;
                yield break;
            }

            yield return null;
        }
    }

    IEnumerator InvestigateTick()
    {
        while (state == AiState.InvestigateSound && agent.pathPending)
            yield return null;

        while (state == AiState.InvestigateSound
               && agent.isOnNavMesh
               && agent.remainingDistance > agent.stoppingDistance + 0.15f)
        {
            yield return null;
        }

        if (state != AiState.InvestigateSound)
            yield break;

        if (logStates)
            Debug.Log($"[{name}] Investigación sin contacto visual → retomar", this);

        hasInvestigateTarget = false;
        ResumeInterrupted("investigate done");
    }

    IEnumerator FleeTick()
    {
        if (agent != null)
        {
            agent.enabled = true;
            agent.isStopped = false;
            agent.speed = fleeSpeed;
        }

        Vector3 from = perception != null ? perception.LastSeenPosition : transform.position;
        if (perception != null && perception.LastSeenTarget != null)
            from = perception.LastSeenTarget.transform.position;

        // Si no hay amenaza registrada (foto sin posición), huir "hacia atrás".
        if ((from - transform.position).sqrMagnitude < 0.01f)
            from = transform.position - transform.forward * 2f;

        if (!TryFindFarthestPoint(from, out Vector3 fleePoint))
            fleePoint = transform.position + (transform.position - from).normalized * 12f;

        if (agent != null && agent.isOnNavMesh)
            agent.SetDestination(fleePoint);

        if (logStates)
            Debug.Log($"[{name}] HUYE hacia {fleePoint}", this);

        float fleeStart = Time.time;

        while (state == AiState.Flee && agent != null && agent.pathPending)
            yield return null;

        while (state == AiState.Flee)
        {
            float elapsed = Time.time - fleeStart;
            bool minTimeDone = elapsed >= minFleeDuration;
            bool arrived = agent != null
                           && agent.enabled
                           && agent.isOnNavMesh
                           && !agent.pathPending
                           && agent.hasPath
                           && agent.pathStatus != NavMeshPathStatus.PathInvalid
                           && agent.remainingDistance <= agent.stoppingDistance + 0.25f;
            bool pathFailed = agent != null
                              && agent.enabled
                              && !agent.pathPending
                              && (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid);

            // No ocultar hasta haber corrido el mínimo (salvo que ya no estemos en Flee).
            if (minTimeDone && (arrived || pathFailed || elapsed >= minFleeDuration * 3f))
                break;

            yield return null;
        }

        if (state != AiState.Flee)
            yield break;

        EnterHidden();
    }

    void EnterHidden()
    {
        preHidePosition = transform.position;
        actionInProgress = false;
        actionTimeRemaining = 0f;
        resumeActivity = InterruptedActivity.None;

        if (agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.isStopped = true;
        }

        Vector3 hidePos = preHidePosition + hideTeleportOffset;
        agent.enabled = false;
        transform.position = hidePos;
        SetVisualsHidden(true);
        SetState(AiState.Hidden, $"teleport {hidePos}");
    }

    IEnumerator HiddenTick()
    {
        if (fleeAndStayHidden)
        {
            if (logStates)
                Debug.Log($"[{name}] Oculto permanente (fotografiado en acción).", this);

            while (state == AiState.Hidden)
                yield return null;
            yield break;
        }

        float hideTime = Random.Range(hideDurationMin, hideDurationMax);
        if (logStates)
            Debug.Log($"[{name}] Oculto {hideTime:F1}s", this);

        float end = Time.time + hideTime;
        while (state == AiState.Hidden && Time.time < end)
            yield return null;

        if (state != AiState.Hidden)
            yield break;

        Reappear();
    }

    /// <summary>
    /// Llamado al capturar una foto válida de la acción polémica.
    /// Huye y no vuelve a aparecer en esta ronda.
    /// </summary>
    public void NotifyPhotographedDuringAction()
    {
        NotifyPhotographedDuringAction(transform.position - transform.forward * 3f);
    }

    public void NotifyPhotographedDuringAction(Vector3 photographerWorldPosition)
    {
        if (!isActiveAndEnabled || visualsHidden)
            return;

        fleeAndStayHidden = true;
        if (perception != null)
            perception.RegisterThreatPosition(photographerWorldPosition);

        if (logStates)
            Debug.Log($"[{name}] ¡Fotografiado en acción! Huida permanente.", this);

        if (state == AiState.Flee || state == AiState.Hidden)
            return;

        InterruptForFlee("fotografiado");
    }

    void Reappear()
    {
        Vector3 spawn = preHidePosition;
        if (!CelebrityRoomUtility.TryPickRandomNavMeshInRooms(3f, 40, out spawn))
        {
            if (NavMesh.SamplePosition(preHidePosition, out NavMeshHit hit, 20f, NavMesh.AllAreas))
                spawn = hit.position;
            else
                spawn = roamOrigin;
        }

        transform.position = spawn;
        agent.enabled = true;
        if (NavMesh.SamplePosition(spawn, out NavMeshHit snap, 5f, NavMesh.AllAreas))
            agent.Warp(snap.position);

        SetVisualsHidden(false);
        agent.speed = walkSpeed;
        ResetRoutineAfterSpawnOrReappear(initial: false);
    }

    void SetVisualsHidden(bool hidden)
    {
        visualsHidden = hidden;
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = !hidden;
            }
        }

        // Colliders del cuerpo: evitar bloqueos mientras está oculto.
        if (colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = !hidden;
            }
        }
    }

    void OnSawTarget(PerceptionTarget target, Vector3 aim)
    {
        if (!isActiveAndEnabled || visualsHidden)
            return;
        if (state == AiState.Flee || state == AiState.Hidden)
            return;

        InterruptForFlee();
    }

    void InterruptForFlee(string reason = "vió jugador")
    {
        // Cancela cualquier actividad (incluida acción). No se retoma tras huir.
        resumeActivity = InterruptedActivity.None;
        actionInProgress = false;
        actionTimeRemaining = 0f;
        hasInvestigateTarget = false;
        investigateArmed = false;

        if (agent != null)
        {
            agent.enabled = true;
            agent.isStopped = false;
            agent.speed = fleeSpeed;
        }

        SetState(AiState.Flee, reason);
    }

    void OnHeardNoise(NoiseEventData noise, float effectiveLoudness, int walls)
    {
        if (!isActiveAndEnabled || visualsHidden)
            return;
        if (state == AiState.Flee || state == AiState.Hidden)
            return;

        if (state == AiState.InvestigateSound)
        {
            // Ignora nuevos ruidos hasta llegar.
            return;
        }

        if (state == AiState.ListenIdle)
        {
            float elapsed = Time.time - listenStartedTime;
            if (elapsed < listenIgnoreNoiseSeconds)
                return;

            investigateTarget = noise.Position;
            hasInvestigateTarget = true;
            investigateArmed = true;
            if (logStates)
                Debug.Log($"[{name}] 2º ruido → investigar {investigateTarget}", this);
            return;
        }

        // Primer ruido: guardar actividad e idle escuchar.
        SaveInterruptedFromCurrent();
        listenStartedTime = Time.time;
        investigateArmed = false;
        hasInvestigateTarget = false;
        SetState(AiState.ListenIdle, noise.Type.ToString());
    }

    void SaveInterruptedFromCurrent()
    {
        switch (state)
        {
            case AiState.WalkToAction:
                resumeActivity = InterruptedActivity.WalkToAction;
                break;
            case AiState.PerformAction:
                resumeActivity = InterruptedActivity.PerformAction;
                // actionTimeRemaining ya tiene lo que falta.
                break;
            default:
                resumeActivity = InterruptedActivity.Patrol;
                break;
        }
    }

    void ResumeInterrupted(string reason)
    {
        agent.isStopped = false;
        agent.speed = walkSpeed;

        switch (resumeActivity)
        {
            case InterruptedActivity.WalkToAction:
                SetState(AiState.WalkToAction, reason);
                if (CelebrityRoundState.Instance != null && CelebrityRoundState.Instance.HasLocationPoint)
                    agent.SetDestination(CelebrityRoundState.Instance.LocationPoint);
                break;
            case InterruptedActivity.PerformAction:
                StartPerformAction();
                break;
            default:
                SetState(AiState.Patrol, reason);
                break;
        }

        resumeActivity = InterruptedActivity.None;
    }

    bool TryPickPatrolDestination(Vector3 preferAway, out Vector3 destination)
    {
        if (patrol != null)
            return patrol.TryPickDestinationPublic(preferAway, out destination);

        Vector3 center = roamFromSpawn ? roamOrigin : transform.position;
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

    bool TryFindFarthestPoint(Vector3 fromPlayer, out Vector3 farthest)
    {
        farthest = transform.position;
        float best = -1f;
        Vector3 center = roamFromSpawn ? roamOrigin : transform.position;

        for (int i = 0; i < fleeSampleAttempts; i++)
        {
            Vector2 circle = Random.insideUnitCircle * fleeSearchRadius;
            Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                continue;

            float d = (hit.position - fromPlayer).sqrMagnitude;
            if (d > best)
            {
                best = d;
                farthest = hit.position;
            }
        }

        return best > 0f;
    }

    void SetState(AiState next, string reason)
    {
        if (state == next && reason != "spawn")
        {
            // Permitir reentrada en algunos casos; solo log si cambia.
        }

        AiState prev = state;
        state = next;
        if (logStates)
            Debug.Log($"[{name}] AI {prev} → {next} ({reason})", this);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (CelebrityRoundState.Instance != null && CelebrityRoundState.Instance.HasLocationPoint)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(CelebrityRoundState.Instance.LocationPoint, 0.35f);
        }

        if (hasInvestigateTarget)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(investigateTarget, 0.3f);
        }
    }
#endif
}
