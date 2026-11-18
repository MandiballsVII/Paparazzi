using System;
using FishNet;
using FishNet.Object;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Sensor de presión: jugadores (lista PerceptionTarget + owner) y props (trigger server); NPCs no.
/// Dispara al notar peso; tras cooldown puede volver a disparar.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PressurePlateSensor : NetworkBehaviour
{
    [SerializeField]
    float cooldownSeconds = 5f;

    [SerializeField]
    float surfaceSampleDistance = 0.35f;

    [SerializeField]
    float playerPollInterval = 0.05f;

    [SerializeField]
    float playerSamplePadding = 0.12f;

    [SerializeField]
    LayerMask surfaceSampleMask = ~0;

    [SerializeField]
    UnityEvent onTrapActivated;

    [SerializeField]
    MeshRenderer plateRenderer;

    [SerializeField]
    BoxCollider triggerCollider;

    float nextReadyUnscaledTime;
    float nextPlayerPollUnscaledTime;
    bool loggedMissingSpawn;

    public event Action<string, bool> Activated;

    public float CooldownSeconds => cooldownSeconds;
    public bool IsReady => Time.unscaledTime >= nextReadyUnscaledTime;

    void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;

        if (!TryGetComponent(out Rigidbody body))
        {
            body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }
    }

    void Awake()
    {
        if (plateRenderer == null)
            TryGetComponent(out plateRenderer);

        if (triggerCollider == null)
            TryGetComponent(out triggerCollider);

        EnsureKinematicBody();
    }

    void EnsureKinematicBody()
    {
        if (!TryGetComponent(out Rigidbody body))
        {
            body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            return;
        }

        body.isKinematic = true;
        body.useGravity = false;
    }

    void Start()
    {
        TrySampleSurfaceMaterial();
    }

    void Update()
    {
        if (Time.unscaledTime < nextPlayerPollUnscaledTime)
            return;

        nextPlayerPollUnscaledTime = Time.unscaledTime + Mathf.Max(0.02f, playerPollInterval);
        PollLocalPlayers();
    }

    /// <summary>
    /// Local split-screen: FishNet desactiva NetworkObjects de escena sin red.
    /// </summary>
    public static void ActivateAllForOfflinePlay()
    {
        PressurePlateSensor[] plates = UnityEngine.Object.FindObjectsByType<PressurePlateSensor>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < plates.Length; i++)
        {
            PressurePlateSensor plate = plates[i];
            if (plate == null)
                continue;

            if (plate.TryGetComponent(out NetworkObject nob))
                nob.SetIsNetworked(false);

            plate.gameObject.SetActive(true);
            plate.TrySampleSurfaceMaterial();
        }
    }

    public void TrySampleSurfaceMaterial()
    {
        if (plateRenderer == null)
            TryGetComponent(out plateRenderer);
        if (plateRenderer == null)
            return;

        Vector3 origin = transform.position;
        Vector3[] directions =
        {
            -transform.up,
            transform.up,
            -transform.forward,
            transform.forward,
            -transform.right,
            transform.right
        };

        for (int i = 0; i < directions.Length; i++)
        {
            if (!Physics.Raycast(
                    origin,
                    directions[i],
                    out RaycastHit hit,
                    surfaceSampleDistance,
                    surfaceSampleMask,
                    QueryTriggerInteraction.Ignore))
                continue;

            if (hit.collider != null && hit.collider.transform.IsChildOf(transform))
                continue;

            Renderer surfaceRenderer = hit.collider.GetComponent<Renderer>();
            if (surfaceRenderer == null)
                surfaceRenderer = hit.collider.GetComponentInParent<Renderer>();
            if (surfaceRenderer == null || surfaceRenderer == plateRenderer)
                continue;
            if (surfaceRenderer.sharedMaterial == null)
                continue;

            plateRenderer.sharedMaterial = surfaceRenderer.sharedMaterial;
            return;
        }
    }

    void PollLocalPlayers()
    {
        if (triggerCollider == null && !TryGetComponent(out triggerCollider))
            return;

        // No OverlapBox: CharacterController + NetworkTransform no es fiable en queries online.
        // Recorremos PerceptionTarget (jugadores) y comprobamos pies dentro del volumen OBB.
        var targets = PerceptionTarget.ActiveTargets;
        for (int i = 0; i < targets.Count; i++)
        {
            PerceptionTarget target = targets[i];
            if (target == null)
                continue;

            if (!IsLocallyControlledPlayer(target))
                continue;

            if (target.GetComponentInParent<CelebrityBrain>() != null)
                continue;

            if (!IsPlayerSampleInsidePlate(target, out string activatorName))
                continue;

            ReportPlayerActivation(activatorName);
            return;
        }
    }

    bool IsPlayerSampleInsidePlate(PerceptionTarget target, out string activatorName)
    {
        activatorName = target.name;

        // OBB local: válido también si la placa está en pared/techo.
        Vector3 worldCenter = triggerCollider.transform.TransformPoint(triggerCollider.center);
        Vector3 halfExtents = Vector3.Scale(triggerCollider.size * 0.5f, triggerCollider.transform.lossyScale);
        halfExtents += Vector3.one * Mathf.Max(0f, playerSamplePadding);

        Vector3 sample = GetPlayerSamplePoint(target, worldCenter);
        Vector3 local = Quaternion.Inverse(triggerCollider.transform.rotation) * (sample - worldCenter);
        return Mathf.Abs(local.x) <= halfExtents.x
               && Mathf.Abs(local.y) <= halfExtents.y
               && Mathf.Abs(local.z) <= halfExtents.z;
    }

    static Vector3 GetPlayerSamplePoint(PerceptionTarget target, Vector3 plateWorldCenter)
    {
        // Punto del cuerpo más cercano al centro de la placa (suelo o pared).
        if (target.TryGetComponent(out CharacterController cc))
            return cc.bounds.ClosestPoint(plateWorldCenter);

        return target.transform.position;
    }

    void ReportPlayerActivation(string activatorName)
    {
        if (!IsNetworkSessionActive())
        {
            ProcessActivation(activatorName);
            return;
        }

        if (IsServerInitialized)
        {
            ProcessActivation(activatorName);
            return;
        }

        if (IsClientInitialized && IsSpawned)
        {
            RequestActivationServerRpc(activatorName);
            return;
        }

        // Placa de escena sin spawn: el ServerRpc no puede salir. Evitar silencio total.
        if (loggedMissingSpawn)
            return;

        loggedMissingSpawn = true;
        Debug.LogWarning(
            $"[PressurePlate:{gameObject.name}] cliente sin IsSpawned; no se puede avisar al server. " +
            "Reserialize NetworkObject de la placa en la escena.");
    }

    void OnTriggerEnter(Collider other)
    {
        // Props (y fallback): física server / offline.
        if (!TryResolveActivator(other, out string activatorName, out bool isPlayer))
            return;

        if (isPlayer)
            return; // Jugadores van por PollLocalPlayers (lista PerceptionTarget).

        if (!IsNetworkSessionActive() || IsServerInitialized)
            ProcessActivation(activatorName);
    }

    static bool IsLocallyControlledPlayer(Component source)
    {
        NetworkObject nob = source.GetComponentInParent<NetworkObject>();
        if (nob != null && nob.IsSpawned)
            return nob.IsOwner;

        // Offline / placa o jugador aún sin spawn de red.
        return source.GetComponentInParent<PlayerHealth>() != null
               || source.GetComponentInParent<PerceptionTarget>() != null;
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestActivationServerRpc(string activatorName)
    {
        ProcessActivation(activatorName);
    }

    void ProcessActivation(string activatorName)
    {
        bool trapActivated = IsReady;
        if (trapActivated)
            nextReadyUnscaledTime = Time.unscaledTime + Mathf.Max(0f, cooldownSeconds);

        LogActivation(activatorName, trapActivated);
        RaiseActivation(activatorName, trapActivated);

        if (IsNetworkSessionActive() && IsServerInitialized)
            ActivationFeedbackObserversRpc(activatorName, trapActivated);
    }

    [ObserversRpc]
    void ActivationFeedbackObserversRpc(string activatorName, bool trapActivated)
    {
        if (IsServerInitialized)
            return;

        LogActivation(activatorName, trapActivated);
    }

    void LogActivation(string activatorName, bool trapActivated)
    {
        Debug.Log(
            $"[PressurePlate:{gameObject.name}] pisado por '{activatorName}' → " +
            $"trampa {(trapActivated ? "ACTIVADA" : "NO (cooldown)")}");
    }

    void RaiseActivation(string activatorName, bool trapActivated)
    {
        Activated?.Invoke(activatorName, trapActivated);
        if (trapActivated)
            onTrapActivated?.Invoke();
    }

    static bool TryResolveActivator(Collider other, out string activatorName, out bool isPlayer)
    {
        activatorName = other != null ? other.name : "null";
        isPlayer = false;
        if (other == null)
            return false;

        Transform root = other.transform;

        if (root.GetComponentInParent<CelebrityBrain>() != null)
            return false;

        if (root.GetComponentInParent<PerceptionAgent>() != null
            && root.GetComponentInParent<PerceptionTarget>() == null
            && root.GetComponentInParent<PlayerHealth>() == null)
            return false;

        PerceptionTarget perceptionTarget = root.GetComponentInParent<PerceptionTarget>();
        if (perceptionTarget != null)
        {
            activatorName = perceptionTarget.name;
            isPlayer = true;
            return true;
        }

        PlayerHealth health = root.GetComponentInParent<PlayerHealth>();
        if (health != null)
        {
            activatorName = health.name;
            isPlayer = true;
            return true;
        }

        Rigidbody body = root.GetComponentInParent<Rigidbody>();
        if (body != null && !body.isKinematic)
        {
            if (body.TryGetComponent(out PressurePlateSensor _))
                return false;

            activatorName = body.name;
            isPlayer = false;
            return true;
        }

        return false;
    }

    static bool IsNetworkSessionActive()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null)
            return false;
        return nm.IsServerStarted || nm.IsClientStarted;
    }
}
