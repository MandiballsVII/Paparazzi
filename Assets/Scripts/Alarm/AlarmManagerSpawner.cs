using FishNet;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// Spawnea AlarmManager en online si aún no existe (p.ej. RoundSession sin el componente).
/// </summary>
public class AlarmManagerSpawner : MonoBehaviour
{
    [SerializeField]
    NetworkObject alarmManagerPrefab;

    bool spawned;

    void OnEnable()
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null)
            nm = FindAnyObjectByType<NetworkManager>();
        if (nm == null)
            return;

        nm.ServerManager.OnServerConnectionState += OnServerConnectionState;
        if (nm.IsServerStarted)
            TrySpawn();
    }

    void OnDisable()
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null)
            return;
        nm.ServerManager.OnServerConnectionState -= OnServerConnectionState;
    }

    void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
            TrySpawn();
    }

    void TrySpawn()
    {
        if (spawned)
            return;

        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null || !nm.IsServerStarted)
            return;

        if (AlarmManager.Instance != null
            && AlarmManager.Instance.TryGetComponent(out NetworkObject existing)
            && existing.IsSpawned)
        {
            spawned = true;
            return;
        }

        // Si RoundSession ya trae AlarmManager, basta con ese.
        NetworkAlarmManager[] existingAlarms =
            FindObjectsByType<NetworkAlarmManager>(FindObjectsSortMode.None);
        for (int i = 0; i < existingAlarms.Length; i++)
        {
            if (existingAlarms[i] != null
                && existingAlarms[i].TryGetComponent(out NetworkObject nob)
                && nob.IsSpawned)
            {
                spawned = true;
                return;
            }
        }

        // Preferible: AlarmManager ya colocado en la escena Gameplay (editable).
        AlarmManager sceneAlarm = FindAnyObjectByType<AlarmManager>();
        if (sceneAlarm != null && sceneAlarm.TryGetComponent(out NetworkObject _))
        {
            spawned = true;
            return;
        }

        NetworkObject prefab = alarmManagerPrefab;
        if (prefab == null)
            prefab = FindSpawnableByName("AlarmManager");

        if (prefab == null)
        {
            Debug.LogWarning(
                "[Alarm] Falta AlarmManager en escena / spawnable. " +
                "Coloca el prefab en Gameplay o Tools > Paparazzi > Setup Alarm Manager Prefab.");
            return;
        }

        NetworkObject instance = nm.GetPooledInstantiated(prefab, Vector3.zero, Quaternion.identity, asServer: true);
        nm.ServerManager.Spawn(instance);
        spawned = true;
    }

    static NetworkObject FindSpawnableByName(string objectName)
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null || nm.SpawnablePrefabs == null)
            return null;

        int count = nm.SpawnablePrefabs.GetObjectCount();
        for (int i = 0; i < count; i++)
        {
            NetworkObject candidate = nm.SpawnablePrefabs.GetObject(asServer: true, i);
            if (candidate != null && candidate.name == objectName)
                return candidate;
        }

        return null;
    }
}
