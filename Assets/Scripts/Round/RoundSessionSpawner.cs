using FishNet;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// Spawnea NetworkRoundExtract cuando el server está listo.
/// </summary>
public class RoundSessionSpawner : MonoBehaviour
{
    [SerializeField]
    NetworkObject roundSessionPrefab;

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

        if (NetworkRoundExtract.Instance != null
            && NetworkRoundExtract.Instance.TryGetComponent(out NetworkObject existing)
            && existing.IsSpawned)
        {
            spawned = true;
            return;
        }

        NetworkObject prefab = roundSessionPrefab;
        if (prefab == null)
            prefab = FindSpawnableByName("RoundSession");

        if (prefab == null)
        {
            Debug.LogError(
                "[RoundSessionSpawner] Falta RoundSession prefab. " +
                "Ejecuta Tools > Paparazzi > Setup Round Session Prefab.");
            return;
        }

        NetworkObject nob = nm.GetPooledInstantiated(prefab, Vector3.zero, Quaternion.identity, asServer: true);
        nm.ServerManager.Spawn(nob);
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
