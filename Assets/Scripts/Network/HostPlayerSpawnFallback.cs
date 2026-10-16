using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

public class HostPlayerSpawnFallback : MonoBehaviour
{
    [SerializeField] NetworkObject playerPrefab;
    [SerializeField] Transform[] spawns;

    NetworkManager nm;
    bool spawnedLocal;

    void OnEnable()
    {
        nm = InstanceFinder.NetworkManager;
        if (nm == null) return;
        nm.ServerManager.OnAuthenticationResult += OnAuth;
    }

    void OnDisable()
    {
        if (nm != null)
            nm.ServerManager.OnAuthenticationResult -= OnAuth;
    }

    void OnAuth(NetworkConnection conn, bool authenticated)
    {
        if (!authenticated || !nm.IsServerStarted || playerPrefab == null)
            return;

        // Evita duplicar si PlayerSpawner ya spawneó
        if (conn.FirstObject != null)
            return;

        Vector3 pos = spawns != null && spawns.Length > 0 && spawns[0] != null
            ? spawns[0].position
            : new Vector3(0f, 1f, 6f);
        Quaternion rot = spawns != null && spawns.Length > 0 && spawns[0] != null
            ? spawns[0].rotation
            : Quaternion.Euler(0f, 180f, 0f);

        NetworkObject nob = nm.GetPooledInstantiated(playerPrefab, pos, rot, true);
        nm.ServerManager.Spawn(nob, conn);
        spawnedLocal = true;
        Debug.Log($"Fallback spawn player for client {conn.ClientId}");
    }
}