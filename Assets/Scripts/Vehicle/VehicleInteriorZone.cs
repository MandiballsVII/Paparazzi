using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zona interior de la furgoneta. Ocupación por posición del transform
/// (no depende de CharacterController activo: remotes online lo tienen off).
/// </summary>
[RequireComponent(typeof(Collider))]
public class VehicleInteriorZone : MonoBehaviour
{
    [SerializeField]
    float boundsMargin = 0.2f;

    [SerializeField]
    float sampleHeight = 0.9f;

    readonly List<PlayerHealth> livingBuffer = new(8);

    BoxCollider box;

    void Awake()
    {
        TryGetComponent(out box);
    }

    public IReadOnlyList<PlayerHealth> GetLivingOccupants()
    {
        livingBuffer.Clear();

        if (box == null && !TryGetComponent(out box))
            return livingBuffer;

        PlayerHealth[] all = Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            PlayerHealth health = all[i];
            if (health == null || health.IsDead)
                continue;

            // Pies + pecho: evita fallos si el pivot está en el suelo o el techo del van es bajo.
            Vector3 feet = health.transform.position;
            Vector3 chest = feet + Vector3.up * sampleHeight;
            if (IsWorldPointInside(feet) || IsWorldPointInside(chest))
                livingBuffer.Add(health);
        }

        return livingBuffer;
    }

    public bool IsInside(PlayerHealth health)
    {
        if (health == null)
            return false;

        IReadOnlyList<PlayerHealth> living = GetLivingOccupants();
        for (int i = 0; i < living.Count; i++)
        {
            if (living[i] == health)
                return true;
        }

        return false;
    }

    bool IsWorldPointInside(Vector3 worldPoint)
    {
        // Caja orientada (local): fiable si la furgoneta está rotada.
        Vector3 local = box.transform.InverseTransformPoint(worldPoint) - box.center;
        Vector3 half = box.size * 0.5f + Vector3.one * boundsMargin;
        return Mathf.Abs(local.x) <= half.x
               && Mathf.Abs(local.y) <= half.y
               && Mathf.Abs(local.z) <= half.z;
    }
}
