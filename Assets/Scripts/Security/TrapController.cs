using System;
using System.Collections;
using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Trampilla de doble hoja. Dueña del cooldown y del modo de sensor (placa o IR).
/// </summary>
public class TrapController : NetworkBehaviour
{
    [SerializeField]
    Transform pivotLeft;

    [SerializeField]
    Transform pivotRight;

    [SerializeField]
    MeshRenderer panelLeftRenderer;

    [SerializeField]
    MeshRenderer panelRightRenderer;

    [SerializeField]
    GameObject pressureSensorRoot;

    [SerializeField]
    GameObject infraredSensorRoot;

    [SerializeField]
    TrapSensorMode activeMode = TrapSensorMode.PressurePlate;

    [SerializeField]
    float openAngleDegrees = 90f;

    [SerializeField]
    float openDurationSeconds = 0.12f;

    [SerializeField]
    float closeDurationSeconds = 0.2f;

    [SerializeField]
    float cooldownSeconds = 5f;

    [SerializeField]
    float floorSampleMargin = 0.6f;

    [SerializeField]
    float floorSampleHeightOffset = 0.08f;

    [SerializeField]
    float floorSampleDownBias = 0.08f;

    [SerializeField]
    float floorSampleOriginInset = 0.1f;

    [SerializeField]
    float exitOutwardNudge = 0.35f;

    [SerializeField]
    LayerMask floorSampleMask = 0; // 0 → Floor+Walls en runtime

    [SerializeField]
    LayerMask exitClearanceMask = ~0;

    [SerializeField]
    bool debugFloorSampleRays;

    [SerializeField]
    bool applySensorModeOnAwake = true;

    Quaternion leftClosedRotation;
    Quaternion rightClosedRotation;
    Quaternion leftOpenRotation;
    Quaternion rightOpenRotation;

    float nextArmedUnscaledTime;
    bool isOpen;
    bool isAnimating;
    Coroutine motionRoutine;

    public TrapSensorMode ActiveMode => activeMode;
    public float CooldownSeconds => cooldownSeconds;
    public bool IsOpen => isOpen;
    public bool IsArmed => !isOpen && !isAnimating && Time.unscaledTime >= nextArmedUnscaledTime;

    void Awake()
    {
        CachePanelRenderers();
        CacheRotations();
        if (applySensorModeOnAwake)
            ApplySensorRoots();
    }

    void Start()
    {
        TrySampleAdjacentFloorMaterial();
    }

    void Update()
    {
        if (!debugFloorSampleRays)
            return;

        if (!TryBuildFloorSampleRays(out Vector3[] origins, out Vector3[] directions, out float reach, out _, out _))
            return;

        for (int i = 0; i < origins.Length; i++)
            Debug.DrawRay(origins[i], directions[i] * reach, Color.cyan, 0f, false);
    }

    void OnDrawGizmosSelected()
    {
        if (!TryBuildFloorSampleRays(out Vector3[] origins, out Vector3[] directions, out float reach, out _, out _))
            return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < origins.Length; i++)
        {
            Gizmos.DrawSphere(origins[i], 0.03f);
            Gizmos.DrawLine(origins[i], origins[i] + directions[i] * reach);
        }
    }

    void CacheRotations()
    {
        if (pivotLeft != null)
        {
            leftClosedRotation = pivotLeft.localRotation;
            leftOpenRotation = leftClosedRotation * Quaternion.Euler(0f, 0f, -openAngleDegrees);
        }

        if (pivotRight != null)
        {
            rightClosedRotation = pivotRight.localRotation;
            rightOpenRotation = rightClosedRotation * Quaternion.Euler(0f, 0f, openAngleDegrees);
        }
    }

    /// <summary>
    /// Local split-screen: FishNet desactiva NetworkObjects de escena sin red.
    /// </summary>
    public static void ActivateAllForOfflinePlay()
    {
        TrapController[] traps = UnityEngine.Object.FindObjectsByType<TrapController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < traps.Length; i++)
        {
            TrapController trap = traps[i];
            if (trap == null)
                continue;

            if (trap.TryGetComponent(out NetworkObject nob))
                nob.SetIsNetworked(false);

            trap.gameObject.SetActive(true);
            trap.ApplySensorRoots();
            trap.TrySampleAdjacentFloorMaterial();
        }
    }

    void CachePanelRenderers()
    {
        if (panelLeftRenderer == null)
            panelLeftRenderer = FindDoorPanelRenderer(pivotLeft);
        if (panelRightRenderer == null)
            panelRightRenderer = FindDoorPanelRenderer(pivotRight);
    }

    static MeshRenderer FindDoorPanelRenderer(Transform pivot)
    {
        if (pivot == null)
            return null;

        for (int i = 0; i < pivot.childCount; i++)
        {
            Transform child = pivot.GetChild(i);
            if (child.name.StartsWith("Panel", StringComparison.OrdinalIgnoreCase)
                && child.TryGetComponent(out MeshRenderer namedPanel))
                return namedPanel;
        }

        for (int i = 0; i < pivot.childCount; i++)
        {
            Transform child = pivot.GetChild(i);
            if (child.TryGetComponent(out MeshRenderer anyPanel))
                return anyPanel;
        }

        return null;
    }

    /// <summary>
    /// Copia el material de un suelo adyacente (layer Floor).
    /// Walls bloquean el rayo (no coger Floor al otro lado de la pared).
    /// Origen en el borde del footprint + ligera caída. Prueba 4 lados.
    /// </summary>
    public void TrySampleAdjacentFloorMaterial()
    {
        CachePanelRenderers();
        if (panelLeftRenderer == null && panelRightRenderer == null)
        {
            Debug.LogWarning(
                $"[Trap:{gameObject.name}] sample Floor: sin panel renderers (¿PanelL/PanelR?).");
            return;
        }

        if (!TryBuildFloorSampleRays(
                out Vector3[] origins,
                out Vector3[] directions,
                out float reach,
                out string[] sideNames,
                out _))
        {
            Debug.LogWarning($"[Trap:{gameObject.name}] sample Floor: no se pudo calcular rayos.");
            return;
        }

        for (int i = 0; i < origins.Length; i++)
        {
            if (!TryGetFloorHit(origins[i], directions[i], reach, out RaycastHit hit, out Renderer surfaceRenderer))
                continue;

            ApplyFloorMaterial(surfaceRenderer.sharedMaterial);
            Debug.Log(
                $"[Trap:{gameObject.name}] material Floor (layer) desde '{hit.collider.name}' " +
                $"(lado {sideNames[i]}, origin={origins[i]}, dist {hit.distance:0.###}, reach {reach:0.###}).");
            return;
        }

        Debug.LogWarning(
            $"[Trap:{gameObject.name}] sample Floor falló: ningún hit válido en 4 lados " +
            $"(reach={reach:0.###}, heightOff={floorSampleHeightOffset}, downBias={floorSampleDownBias}). " +
            "Activa debugFloorSampleRays y Gizmos. ¿Suelo en layer Floor y paredes en Walls?");
    }

    /// <summary>
    /// Punto de pie en un Floor adyacente (fuera del hueco de la trampilla) para sacar al jugador.
    /// </summary>
    public bool TryGetSafeExitWorldPosition(out Vector3 playerWorldPosition, out string sideUsed)
    {
        playerWorldPosition = default;
        sideUsed = null;

        if (!TryBuildFloorSampleRays(
                out Vector3[] origins,
                out Vector3[] directions,
                out float reach,
                out string[] sideNames,
                out Vector3[] horizontals))
            return false;

        float halfHeight = PlayerBodyMetrics.CapsuleHeight * 0.5f;
        float radius = PlayerBodyMetrics.CapsuleRadius;
        float nudge = Mathf.Max(0f, exitOutwardNudge);

        for (int i = 0; i < origins.Length; i++)
        {
            if (!TryGetFloorHit(origins[i], directions[i], reach, out RaycastHit hit, out _))
                continue;

            Vector3 outward = horizontals[i];
            Vector3 feet = hit.point + transform.up * 0.02f + outward * nudge;
            Vector3 center = feet + transform.up * halfHeight;

            if (!IsExitCapsuleClear(center, radius, halfHeight))
                continue;

            playerWorldPosition = center;
            sideUsed = sideNames[i];
            return true;
        }

        return false;
    }

    LayerMask GetFloorSampleQueryMask()
    {
        // Por defecto / Everything → solo Floor + Walls (paredes ocluyen).
        if (floorSampleMask.value == 0 || floorSampleMask.value == ~0)
            return EnvironmentLayers.FloorAndWallsMask;
        return floorSampleMask;
    }

    bool TryGetFloorHit(
        Vector3 origin,
        Vector3 direction,
        float reach,
        out RaycastHit floorHit,
        out Renderer surfaceRenderer)
    {
        floorHit = default;
        surfaceRenderer = null;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            reach,
            GetFloorSampleQueryMask(),
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int h = 0; h < hits.Length; h++)
        {
            RaycastHit hit = hits[h];
            if (hit.collider == null)
                continue;
            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
                continue;

            // Pared antes que suelo → este lado no vale (no atravesar Walls).
            if (EnvironmentLayers.IsWall(hit.collider))
                return false;

            if (!EnvironmentLayers.IsFloor(hit.collider))
                continue;

            Renderer renderer = hit.collider.GetComponent<Renderer>();
            if (renderer == null)
                renderer = hit.collider.GetComponentInParent<Renderer>();
            if (renderer == null)
                continue;
            if (renderer == panelLeftRenderer || renderer == panelRightRenderer)
                continue;

            floorHit = hit;
            surfaceRenderer = renderer;
            return true;
        }

        return false;
    }

    bool IsExitCapsuleClear(Vector3 capsuleCenter, float radius, float halfHeight)
    {
        Vector3 up = transform.up;
        Vector3 p1 = capsuleCenter + up * (halfHeight - radius);
        Vector3 p2 = capsuleCenter - up * (halfHeight - radius);
        Collider[] overlaps = Physics.OverlapCapsule(
            p1,
            p2,
            Mathf.Max(0.05f, radius * 0.9f),
            exitClearanceMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider col = overlaps[i];
            if (col == null)
                continue;
            if (col.transform == transform || col.transform.IsChildOf(transform))
                continue;
            // El suelo bajo los pies no bloquea el spawn.
            if (EnvironmentLayers.IsFloor(col))
                continue;
            return false;
        }

        return true;
    }

    bool TryBuildFloorSampleRays(
        out Vector3[] origins,
        out Vector3[] directions,
        out float reach,
        out string[] sideNames,
        out Vector3[] horizontals)
    {
        origins = null;
        directions = null;
        sideNames = null;
        horizontals = null;
        reach = 0f;

        CachePanelRenderers();
        if (!TryGetCombinedPanelBounds(out Bounds combined))
            return false;

        Vector3 center = combined.center;
        float height = Mathf.Max(0.01f, floorSampleHeightOffset);
        float downBias = Mathf.Max(0.01f, floorSampleDownBias);
        float inset = Mathf.Max(0f, floorSampleOriginInset);
        // Origen más adentro → hay que alargar el reach para seguir alcanzando el Floor exterior.
        reach = (Mathf.Max(0.1f, floorSampleMargin) + inset)
                * Mathf.Sqrt(1f + downBias * downBias);

        Vector3[] axes =
        {
            transform.right,
            -transform.right,
            transform.forward,
            -transform.forward
        };

        sideNames = new[] { "+right", "-right", "+forward", "-forward" };
        origins = new Vector3[4];
        directions = new Vector3[4];
        horizontals = new Vector3[4];

        for (int i = 0; i < 4; i++)
        {
            Vector3 axis = axes[i].normalized;
            float halfAlong = GetBoundsHalfExtentAlong(combined, axis);
            // Un poco hacia el centro: si la trampa está pegada a Walls, el origen no nace dentro de la pared.
            float along = Mathf.Max(0f, halfAlong - inset);
            origins[i] = center + axis * along + transform.up * height;
            directions[i] = (axis - transform.up * downBias).normalized;
            horizontals[i] = axis;
        }

        return true;
    }

    bool TryGetCombinedPanelBounds(out Bounds combined)
    {
        combined = default;
        bool hasBounds = false;

        if (panelLeftRenderer != null)
        {
            combined = panelLeftRenderer.bounds;
            hasBounds = true;
        }

        if (panelRightRenderer != null)
        {
            if (hasBounds)
                combined.Encapsulate(panelRightRenderer.bounds);
            else
            {
                combined = panelRightRenderer.bounds;
                hasBounds = true;
            }
        }

        return hasBounds;
    }

    static float GetBoundsHalfExtentAlong(Bounds bounds, Vector3 worldAxis)
    {
        worldAxis = worldAxis.normalized;
        Vector3 e = bounds.extents;
        return e.x * Mathf.Abs(worldAxis.x)
               + e.y * Mathf.Abs(worldAxis.y)
               + e.z * Mathf.Abs(worldAxis.z);
    }

    void ApplyFloorMaterial(Material material)
    {
        if (material == null)
            return;

        if (panelLeftRenderer != null)
            panelLeftRenderer.sharedMaterial = material;
        if (panelRightRenderer != null)
            panelRightRenderer.sharedMaterial = material;

        PressurePlateSensor[] plates = GetComponentsInChildren<PressurePlateSensor>(true);
        for (int i = 0; i < plates.Length; i++)
        {
            if (plates[i] != null)
                plates[i].ApplySharedMaterial(material);
        }
    }

    /// <summary>
    /// Llamar una vez al inicio de ronda (server / offline).
    /// </summary>
    public void ConfigureSensorMode(TrapSensorMode mode)
    {
        activeMode = mode;
        ApplySensorRoots();

        if (IsNetworkSessionActive() && IsServerInitialized && IsSpawned)
            SensorModeObserversRpc(mode);
    }

    public bool TryTrigger(string activatorName)
    {
        if (!IsArmed)
            return false;

        if (IsNetworkSessionActive())
        {
            if (!IsServerInitialized)
                return false;

            BeginOpenSequence(activatorName);
            OpenSequenceObserversRpc(activatorName);
            return true;
        }

        BeginOpenSequence(activatorName);
        return true;
    }

    /// <summary>
    /// Cliente owner/local: pide al server abrir (placa/IR anidados usan esto).
    /// </summary>
    public void RequestTriggerFromClient(string activatorName)
    {
        if (!IsNetworkSessionActive())
        {
            TryTrigger(activatorName);
            return;
        }

        if (IsServerInitialized)
        {
            TryTrigger(activatorName);
            return;
        }

        if (IsClientInitialized && IsSpawned)
            RequestTriggerServerRpc(activatorName);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestTriggerServerRpc(string activatorName)
    {
        TryTrigger(activatorName);
    }

    void BeginOpenSequence(string activatorName)
    {
        nextArmedUnscaledTime = Time.unscaledTime + Mathf.Max(0f, cooldownSeconds);
        Debug.Log(
            $"[Trap:{gameObject.name}] activada por '{activatorName}' (modo {activeMode}). " +
            $"Abre → cooldown {cooldownSeconds:0.##}s → cierra.");

        if (motionRoutine != null)
            StopCoroutine(motionRoutine);
        motionRoutine = StartCoroutine(OpenThenCloseRoutine());
    }

    IEnumerator OpenThenCloseRoutine()
    {
        isAnimating = true;
        yield return AnimatePivots(leftOpenRotation, rightOpenRotation, openDurationSeconds);
        isOpen = true;
        isAnimating = false;

        float wait = Mathf.Max(0f, nextArmedUnscaledTime - Time.unscaledTime);
        if (wait > 0f)
            yield return new WaitForSecondsRealtime(wait);

        isAnimating = true;
        yield return AnimatePivots(leftClosedRotation, rightClosedRotation, closeDurationSeconds);
        isOpen = false;
        isAnimating = false;
        motionRoutine = null;
    }

    IEnumerator AnimatePivots(Quaternion leftTarget, Quaternion rightTarget, float duration)
    {
        Quaternion leftStart = pivotLeft != null ? pivotLeft.localRotation : Quaternion.identity;
        Quaternion rightStart = pivotRight != null ? pivotRight.localRotation : Quaternion.identity;
        float dur = Mathf.Max(0.01f, duration);
        float t = 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float s = Mathf.Clamp01(t);
            if (pivotLeft != null)
                pivotLeft.localRotation = Quaternion.Slerp(leftStart, leftTarget, s);
            if (pivotRight != null)
                pivotRight.localRotation = Quaternion.Slerp(rightStart, rightTarget, s);
            yield return null;
        }

        if (pivotLeft != null)
            pivotLeft.localRotation = leftTarget;
        if (pivotRight != null)
            pivotRight.localRotation = rightTarget;
    }

    public void ApplySensorRoots()
    {
        if (pressureSensorRoot != null)
            pressureSensorRoot.SetActive(activeMode == TrapSensorMode.PressurePlate);

        if (infraredSensorRoot != null)
            infraredSensorRoot.SetActive(activeMode == TrapSensorMode.Infrared);
    }

    [ObserversRpc]
    void OpenSequenceObserversRpc(string activatorName)
    {
        if (IsServerInitialized)
            return;

        BeginOpenSequence(activatorName);
    }

    [ObserversRpc]
    void SensorModeObserversRpc(TrapSensorMode mode)
    {
        activeMode = mode;
        ApplySensorRoots();
    }

    static bool IsNetworkSessionActive()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null)
            return false;
        return nm.IsServerStarted || nm.IsClientStarted;
    }
}
