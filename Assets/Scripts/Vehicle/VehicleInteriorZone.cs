using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zona interior de la furgoneta (mismo trigger que revive). Rastrea ocupantes.
/// </summary>
[RequireComponent(typeof(Collider))]
public class VehicleInteriorZone : MonoBehaviour
{
    readonly HashSet<PlayerHealth> occupants = new();
    readonly List<PlayerHealth> livingBuffer = new(8);
    readonly List<PlayerHealth> removeBuffer = new(8);

    public IReadOnlyList<PlayerHealth> GetLivingOccupants()
    {
        livingBuffer.Clear();
        removeBuffer.Clear();

        foreach (PlayerHealth health in occupants)
        {
            if (health == null)
            {
                removeBuffer.Add(health);
                continue;
            }

            if (!health.IsDead)
                livingBuffer.Add(health);
        }

        for (int i = 0; i < removeBuffer.Count; i++)
            occupants.Remove(removeBuffer[i]);

        return livingBuffer;
    }

    public bool IsInside(PlayerHealth health)
    {
        return health != null && occupants.Contains(health);
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerHealth health = ResolveHealth(other);
        if (health != null)
            occupants.Add(health);
    }

    void OnTriggerExit(Collider other)
    {
        PlayerHealth health = ResolveHealth(other);
        if (health != null)
            occupants.Remove(health);
    }

    void OnTriggerStay(Collider other)
    {
        PlayerHealth health = ResolveHealth(other);
        if (health != null)
            occupants.Add(health);
    }

    static PlayerHealth ResolveHealth(Collider other)
    {
        if (other == null)
            return null;
        return other.GetComponentInParent<PlayerHealth>();
    }
}
