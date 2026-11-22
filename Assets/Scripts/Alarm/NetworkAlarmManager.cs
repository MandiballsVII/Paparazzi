using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Sincroniza <see cref="AlarmManager"/> en online (autoridad server).
/// Debe vivir en el mismo prefab/escena que AlarmManager (no AddComponent en runtime).
/// </summary>
[RequireComponent(typeof(AlarmManager))]
[RequireComponent(typeof(NetworkObject))]
public class NetworkAlarmManager : NetworkBehaviour
{
    readonly SyncVar<float> syncedAlarm = new(0f);
    readonly SyncVar<float> syncedFloor = new(0f);

    AlarmManager alarm;

    public bool IsNetworkSessionActive
    {
        get
        {
            var nm = InstanceFinder.NetworkManager;
            return nm != null && (nm.IsServerStarted || nm.IsClientStarted);
        }
    }

    public bool IsServerAuthority
    {
        get
        {
            if (!IsNetworkSessionActive)
                return false;
            var nm = InstanceFinder.NetworkManager;
            return nm != null && nm.IsServerStarted;
        }
    }

    public bool ShouldForwardToServer
    {
        get
        {
            if (!IsNetworkSessionActive)
                return false;
            var nm = InstanceFinder.NetworkManager;
            if (nm == null || nm.IsServerStarted)
                return false;
            return nm.IsClientStarted && IsNetworkObjectSpawned();
        }
    }

    void Awake()
    {
        alarm = GetComponent<AlarmManager>();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        syncedAlarm.OnChange += OnSyncedAlarmChanged;
        syncedFloor.OnChange += OnSyncedFloorChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        syncedAlarm.OnChange -= OnSyncedAlarmChanged;
        syncedFloor.OnChange -= OnSyncedFloorChanged;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        syncedAlarm.Value = alarm != null ? alarm.AlarmValue : 0f;
        syncedFloor.Value = alarm != null ? alarm.FloorValue : 0f;
        if (alarm != null)
            alarm.NotifyNetworkAuthorityReady();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!IsServerAuthority && alarm != null)
            alarm.ApplyAuthoritativeState(syncedAlarm.Value, syncedFloor.Value, invokeLevelEvents: false);
    }

    public void PushSync(float alarmValue, float floorValue)
    {
        if (!IsServerAuthority)
            return;

        // Evita ensuciar el SyncVar (y el eco en host) si casi no ha cambiado.
        const float epsilon = 0.0005f;
        if (Mathf.Abs(syncedAlarm.Value - alarmValue) < epsilon
            && Mathf.Abs(syncedFloor.Value - floorValue) < epsilon)
            return;

        syncedAlarm.Value = alarmValue;
        syncedFloor.Value = floorValue;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestAddServerRpc(float baseAmount, bool scaleByLevel)
    {
        if (alarm != null)
            alarm.ApplyAdd(baseAmount, scaleByLevel);
    }

    bool IsNetworkObjectSpawned()
    {
        return TryGetComponent(out NetworkObject nob) && nob != null && nob.IsSpawned;
    }

    void OnSyncedAlarmChanged(float prev, float next, bool asServer)
    {
        // Host: asServer=false en el callback de cliente reaplicaba un valor atrasado → temblor del fill.
        if (asServer || IsServerAuthority || alarm == null)
            return;
        alarm.ApplyAuthoritativeState(next, syncedFloor.Value, invokeLevelEvents: true);
    }

    void OnSyncedFloorChanged(float prev, float next, bool asServer)
    {
        if (asServer || IsServerAuthority || alarm == null)
            return;
        alarm.ApplyAuthoritativeState(syncedAlarm.Value, next, invokeLevelEvents: false);
    }
}
