using UnityEngine;

/// <summary>
/// Colisión de hold con el collider real del objeto.
/// Look: IsPoseBlocked. Movimiento: ResolvePose (saca el objeto y indica corrección).
/// </summary>
public static class HeldObjectHoldQuery
{
    static readonly Collider[] OverlapBuffer = new Collider[32];

    public static float EstimateRadius(Collider col, float fallback = 0.25f)
    {
        if (col == null)
            return fallback;

        Vector3 e = col.bounds.extents;
        return Mathf.Max(0.05f, Mathf.Max(e.x, Mathf.Max(e.y, e.z)));
    }

    public static bool IsPoseBlocked(
        Collider heldCollider,
        Transform heldRoot,
        Transform playerRoot,
        Vector3 worldPosition,
        Quaternion worldRotation,
        int layerMask = Physics.DefaultRaycastLayers)
    {
        if (heldCollider == null || heldRoot == null)
            return false;

        Transform t = heldRoot;
        Vector3 prevPos = t.position;
        Quaternion prevRot = t.rotation;

        t.SetPositionAndRotation(worldPosition, worldRotation);
        Physics.SyncTransforms();

        bool blocked = TryGetPenetration(heldCollider, heldRoot, playerRoot, layerMask, out _, out _);

        t.SetPositionAndRotation(prevPos, prevRot);
        Physics.SyncTransforms();
        return blocked;
    }

    /// <summary>
    /// Resuelve la pose para que el collider no penetre. correction = resolved - desired
    /// (útil para impedir que el player camine atravesando el obstáculo).
    /// </summary>
    public static Vector3 ResolvePose(
        Collider heldCollider,
        Transform heldRoot,
        Transform playerRoot,
        Vector3 desiredPosition,
        Quaternion worldRotation,
        out Vector3 correction,
        float skin = 0.01f,
        int iterations = 4,
        int layerMask = Physics.DefaultRaycastLayers)
    {
        correction = Vector3.zero;
        if (heldCollider == null || heldRoot == null)
            return desiredPosition;

        Transform t = heldRoot;
        Vector3 prevPos = t.position;
        Quaternion prevRot = t.rotation;

        Vector3 pos = desiredPosition;
        t.SetPositionAndRotation(pos, worldRotation);
        Physics.SyncTransforms();

        for (int i = 0; i < iterations; i++)
        {
            if (!TryGetPenetration(heldCollider, heldRoot, playerRoot, layerMask, out Vector3 dir, out float dist))
                break;

            Vector3 step = dir * (dist + skin);
            pos += step;
            correction += step;
            t.SetPositionAndRotation(pos, worldRotation);
            Physics.SyncTransforms();
        }

        t.SetPositionAndRotation(prevPos, prevRot);
        Physics.SyncTransforms();
        return pos;
    }

    static bool TryGetPenetration(
        Collider heldCollider,
        Transform heldRoot,
        Transform playerRoot,
        int layerMask,
        out Vector3 direction,
        out float distance)
    {
        direction = Vector3.zero;
        distance = 0f;

        Bounds bounds = heldCollider.bounds;
        float probe = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)) + 0.08f;

        int count = Physics.OverlapSphereNonAlloc(
            bounds.center,
            probe,
            OverlapBuffer,
            layerMask,
            QueryTriggerInteraction.Ignore);

        float best = 0f;
        Vector3 bestDir = Vector3.zero;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            Collider other = OverlapBuffer[i];
            if (other == null || other == heldCollider)
                continue;

            Transform ot = other.transform;
            if (ot == heldRoot || ot.IsChildOf(heldRoot))
                continue;
            if (playerRoot != null && (ot == playerRoot || ot.IsChildOf(playerRoot)))
                continue;
            if (other is CharacterController cc && playerRoot != null && cc.transform == playerRoot)
                continue;

            if (!Physics.ComputePenetration(
                    heldCollider,
                    heldCollider.transform.position,
                    heldCollider.transform.rotation,
                    other,
                    other.transform.position,
                    other.transform.rotation,
                    out Vector3 dir,
                    out float dist)
                || dist <= 0.001f)
                continue;

            if (dist > best)
            {
                best = dist;
                bestDir = dir;
                found = true;
            }
        }

        if (!found)
            return false;

        direction = bestDir;
        distance = best;
        return true;
    }
}
