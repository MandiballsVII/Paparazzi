using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Instancia el famoso al empezar el nivel en un punto aleatorio del NavMesh
/// (habitaciones Room_*). Sin NetworkObject: local y online usan el mismo prefab.
/// </summary>
public class CelebritySpawner : MonoBehaviour
{
    public static CelebrityBrain SpawnedInstance { get; private set; }

    [SerializeField]
    GameObject celebrityPrefab;

    [SerializeField]
    Vector3 fallbackSpawnPosition = new Vector3(0f, 0.08333302f, 0f);

    [SerializeField]
    float navMeshSampleRadius = 3f;

    [SerializeField]
    int navMeshAttempts = 48;

    [SerializeField]
    int maxNavMeshReadyFrames = 30;

    bool spawnStarted;

    public void SetPrefab(GameObject prefab)
    {
        if (prefab != null)
            celebrityPrefab = prefab;
    }

    public void SpawnAtLevelStart()
    {
        if (spawnStarted)
            return;

        spawnStarted = true;
        StartCoroutine(SpawnWhenNavMeshReady());
    }

    IEnumerator SpawnWhenNavMeshReady()
    {
        if (FindExistingCelebrity(out CelebrityBrain existing))
        {
            SpawnedInstance = existing;
            yield break;
        }

        if (celebrityPrefab == null)
        {
            Debug.LogError(
                "[CelebritySpawner] Falta Celebrity prefab. Asigna Assets/Prefabs/Celebrity.prefab en GameplayBootstrap.");
            yield break;
        }

        Vector3 spawnPos = fallbackSpawnPosition;
        bool found = false;

        for (int frame = 0; frame < maxNavMeshReadyFrames; frame++)
        {
            if (CelebrityRoomUtility.TryPickRandomNavMeshInRooms(navMeshSampleRadius, navMeshAttempts, out spawnPos))
            {
                found = true;
                break;
            }

            if (NavMesh.SamplePosition(fallbackSpawnPosition, out NavMeshHit hit, 50f, NavMesh.AllAreas))
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
                "[CelebritySpawner] No se encontró NavMesh; usando fallback " + spawnPos);
        }

        GameObject instance = Instantiate(celebrityPrefab, spawnPos, Quaternion.identity);
        instance.name = "Celebrity";

        if (instance.TryGetComponent(out NavMeshAgent agent))
        {
            agent.Warp(spawnPos);
            if (!agent.isOnNavMesh
                && NavMesh.SamplePosition(spawnPos, out NavMeshHit snap, 5f, NavMesh.AllAreas))
            {
                agent.Warp(snap.position);
                spawnPos = snap.position;
            }
        }
        else
        {
            instance.transform.position = spawnPos;
        }

        if (instance.TryGetComponent(out CelebrityBrain brain))
            SpawnedInstance = brain;

        Debug.Log($"[CelebritySpawner] Celebrity en {spawnPos}", instance);
    }

    static bool FindExistingCelebrity(out CelebrityBrain brain)
    {
        brain = Object.FindAnyObjectByType<CelebrityBrain>();
        return brain != null;
    }
}
