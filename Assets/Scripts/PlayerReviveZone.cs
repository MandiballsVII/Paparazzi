using UnityEngine;

/// <summary>
/// Zona dentro del vehículo inicial: si un jugador muerto entra (o lo dejan dentro), revive.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PlayerReviveZone : MonoBehaviour
{
    void Reset()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    void OnTriggerStay(Collider other)
    {
        TryRevive(other);
    }

    void OnTriggerEnter(Collider other)
    {
        TryRevive(other);
    }

    static void TryRevive(Collider other)
    {
        if (other == null)
            return;

        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || !health.IsDead)
            return;

        health.Revive();
    }
}
