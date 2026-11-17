using System.Collections;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Local: Instantiate del prefab sin red.
/// Online: solo el server spawnea NetworkCelebrity (clientes reciben réplica).
/// </summary>
public class CelebritySpawner : MonoBehaviour
{
    public static CelebrityBrain SpawnedInstance { get; private set; }

    [SerializeField]
    GameObject localCelebrityPrefab;

    [SerializeField]
    NetworkObject networkCelebrityPrefab;

    [SerializeField]
    Vector3 fallbackSpawnPosition = new Vector3(0f, 0.08333302f, 0f);

    [SerializeField]
    float navMeshSampleRadius = 3f;

    [SerializeField]
    int navMeshAttempts = 48;

    [SerializeField]
    int maxNavMeshReadyFrames = 30;

    bool spawnStarted;

    public void SetLocalPrefab(GameObject prefab)
    {
        if (prefab != null)
            localCelebrityPrefab = prefab;
    }

    public void SetNetworkPrefab(NetworkObject prefab)
    {
        if (prefab != null)
            networkCelebrityPrefab = prefab;
    }

    /// <param name="networked">True = FishNet Spawn (solo server). False = Instantiate local.</param>
    public void SpawnAtLevelStart(bool networked)
    {
        if (spawnStarted)
            return;

        spawnStarted = true;
        StartCoroutine(SpawnWhenNavMeshReady(networked));
    }

    IEnumerator SpawnWhenNavMeshReady(bool networked)
    {
        if (FindExistingCelebrity(out CelebrityBrain existing))
        {
            SpawnedInstance = existing;
            yield break;
        }

        Vector3 spawnPos = fallbackSpawnPosition;
        bool found = false;

        for (int frame = 0; frame < maxNavMeshReadyFrames; frame++)
        {
            if (
                CelebrityRoomUtility.TryPickRandomNavMeshInRooms(
                    navMeshSampleRadius,
                    navMeshAttempts,
                    out spawnPos
                )
            )
            {
                found = true;
                break;
            }

            if (
                NavMesh.SamplePosition(
                    fallbackSpawnPosition,
                    out NavMeshHit hit,
                    50f,
                    NavMesh.AllAreas
                )
            )
            {
                spawnPos = hit.position;
                found = true;
                break;
            }

            yield return null;
        }

        if (!found)
        {
            spawnPos = fallbackSpawnPosition;
            Debug.LogWarning(
                "[CelebritySpawner] No se encontró NavMesh; usando fallback " + spawnPos
            );
        }

        if (networked)
        {
            // StartConnection puede devolver true antes de IsServerStarted.
            const int maxServerWaitFrames = 60;
            for (int i = 0; i < maxServerWaitFrames; i++)
            {
                NetworkManager nm = InstanceFinder.NetworkManager;
                if (nm != null && nm.IsServerStarted)
                    break;
                yield return null;
            }

            SpawnNetworked(spawnPos);
        }
        else
        {
            SpawnLocal(spawnPos);
        }
    }

    void SpawnLocal(Vector3 spawnPos)
    {
        if (localCelebrityPrefab == null)
        {
            Debug.LogError(
                "[CelebritySpawner] Falta Celebrity prefab local (Assets/Prefabs/Celebrity.prefab)."
            );
            return;
        }

        GameObject instance = Instantiate(localCelebrityPrefab, spawnPos, Quaternion.identity);
        instance.name = "Celebrity";
        WarpAgent(instance, spawnPos);

        if (instance.TryGetComponent(out CelebrityBrain brain))
            SpawnedInstance = brain;

        Debug.Log($"[CelebritySpawner] Celebrity local en {spawnPos}", instance);
    }

    void SpawnNetworked(Vector3 spawnPos)
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null || !nm.IsServerStarted)
        {
            Debug.LogError("[CelebritySpawner] Spawn networked requiere server iniciado.");
            return;
        }

        NetworkObject prefab = networkCelebrityPrefab;
        if (prefab == null)
            prefab = FindSpawnableByName("NetworkCelebrity");

        if (prefab == null)
        {
            Debug.LogError(
                "[CelebritySpawner] Falta NetworkCelebrity prefab. "
                    + "Ejecuta Tools > Paparazzi > Setup Network Celebrity Prefab."
            );
            return;
        }

        NetworkObject nob = nm.GetPooledInstantiated(
            prefab,
            spawnPos,
            Quaternion.identity,
            asServer: true
        );
        nob.name = "Celebrity";
        WarpAgent(nob.gameObject, spawnPos);
        nm.ServerManager.Spawn(nob);

        if (nob.TryGetComponent(out CelebrityBrain brain))
            SpawnedInstance = brain;

        Debug.Log($"[CelebritySpawner] Celebrity networked en {spawnPos}", nob);
    }

    static void WarpAgent(GameObject instance, Vector3 spawnPos)
    {
        if (instance.TryGetComponent(out NavMeshAgent agent))
        {
            if (!agent.enabled)
                agent.enabled = true;
            agent.Warp(spawnPos);
            if (
                !agent.isOnNavMesh
                && NavMesh.SamplePosition(spawnPos, out NavMeshHit snap, 5f, NavMesh.AllAreas)
            )
                agent.Warp(snap.position);
        }
        else
        {
            instance.transform.position = spawnPos;
        }
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

    static bool FindExistingCelebrity(out CelebrityBrain brain)
    {
        brain = Object.FindAnyObjectByType<CelebrityBrain>();
        return brain != null;
    }
}
