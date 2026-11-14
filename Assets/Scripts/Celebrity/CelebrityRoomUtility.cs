using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class CelebrityRoomUtility
{
    const string RoomPrefix = "Room_";

    public static List<Collider> FindRoomTriggers()
    {
        var rooms = new List<Collider>(8);
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
            return rooms;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int r = 0; r < roots.Length; r++)
            CollectRoomsRecursive(roots[r].transform, rooms);

        return rooms;
    }

    static void CollectRoomsRecursive(Transform t, List<Collider> rooms)
    {
        if (t.name.StartsWith(RoomPrefix))
        {
            Collider[] cols = t.GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null && cols[i].isTrigger)
                    rooms.Add(cols[i]);
            }
        }

        for (int c = 0; c < t.childCount; c++)
            CollectRoomsRecursive(t.GetChild(c), rooms);
    }

    public static bool TryPickRandomRoomPoint(out Collider room, out Vector3 navPoint, int attempts = 32)
    {
        room = null;
        navPoint = Vector3.zero;

        List<Collider> rooms = FindRoomTriggers();
        if (rooms.Count == 0)
            return false;

        room = rooms[Random.Range(0, rooms.Count)];
        Bounds b = room.bounds;

        for (int i = 0; i < attempts; i++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(b.min.x, b.max.x),
                b.center.y,
                Random.Range(b.min.z, b.max.z));

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            {
                if (IsPointInsideTrigger(room, hit.position) || (hit.position - b.center).sqrMagnitude < b.size.sqrMagnitude)
                {
                    navPoint = hit.position;
                    return true;
                }
            }
        }

        // Fallback: centro de la habitación proyectado a NavMesh.
        if (NavMesh.SamplePosition(b.center, out NavMeshHit centerHit, 5f, NavMesh.AllAreas))
        {
            navPoint = centerHit.position;
            return true;
        }

        return false;
    }

    public static bool TryPickRandomNavMeshInRooms(float sampleRadius, int attempts, out Vector3 point)
    {
        point = Vector3.zero;
        List<Collider> rooms = FindRoomTriggers();
        if (rooms.Count == 0)
            return false;

        for (int i = 0; i < attempts; i++)
        {
            Collider room = rooms[Random.Range(0, rooms.Count)];
            Bounds b = room.bounds;
            Vector3 candidate = new Vector3(
                Random.Range(b.min.x, b.max.x),
                b.center.y,
                Random.Range(b.min.z, b.max.z));

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
        }

        return false;
    }

    static bool IsPointInsideTrigger(Collider col, Vector3 worldPoint)
    {
        Vector3 closest = col.ClosestPoint(worldPoint);
        return (closest - worldPoint).sqrMagnitude < 0.0001f;
    }
}
