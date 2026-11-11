using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Hace que props dinámicos (caja/esfera) sean obstáculos de NavMesh.
/// Se desactiva mientras el prop está kinematic (p.ej. en las manos).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PropNavMeshObstacle : MonoBehaviour
{
    [SerializeField]
    bool carveWhileMoving = true;

    NavMeshObstacle obstacle;
    Rigidbody body;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (!TryGetComponent(out obstacle))
            obstacle = gameObject.AddComponent<NavMeshObstacle>();

        if (!TryGetComponent(out ImpactNoiseEmitter _))
            gameObject.AddComponent<ImpactNoiseEmitter>();

        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = !carveWhileMoving;
        FitToCollider();
        SyncEnabledState();
    }

    void LateUpdate()
    {
        SyncEnabledState();
    }

    void SyncEnabledState()
    {
        if (obstacle == null || body == null)
            return;

        // En manos (kinematic) no tallar el NavMesh.
        obstacle.enabled = !body.isKinematic;
    }

    void FitToCollider()
    {
        if (obstacle == null)
            return;

        if (TryGetComponent(out BoxCollider box))
        {
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = Vector3.Scale(box.size, Absolute(transform.lossyScale));
            return;
        }

        if (TryGetComponent(out SphereCollider sphere))
        {
            obstacle.shape = NavMeshObstacleShape.Capsule;
            float diameter = sphere.radius * 2f * MaxAxis(transform.lossyScale);
            obstacle.center = sphere.center;
            obstacle.height = diameter;
            obstacle.radius = diameter * 0.5f;
            return;
        }

        Bounds b = new Bounds(transform.position, Vector3.one);
        if (TryGetComponent(out Collider col))
            b = col.bounds;

        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = transform.InverseTransformPoint(b.center);
        obstacle.size = b.size;
    }

    static Vector3 Absolute(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static float MaxAxis(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
}
