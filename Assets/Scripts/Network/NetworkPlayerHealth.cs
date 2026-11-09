using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

[RequireComponent(typeof(PlayerHealth))]
public class NetworkPlayerHealth : NetworkBehaviour
{
    readonly SyncVar<float> syncedHealth = new(100f);
    readonly SyncVar<bool> syncedDead = new(false);

    PlayerHealth health;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        syncedHealth.OnChange += OnSyncedHealthChanged;
        syncedDead.OnChange += OnSyncedDeadChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        syncedHealth.OnChange -= OnSyncedHealthChanged;
        syncedDead.OnChange -= OnSyncedDeadChanged;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        syncedHealth.Value = health.MaxHealth;
        syncedDead.Value = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        health.ApplyFullState(syncedHealth.Value, syncedDead.Value);
    }

    public void RequestDamage(float amount)
    {
        if (amount <= 0f)
            return;

        if (IsServerInitialized)
            ApplyDamageServer(amount);
        else
            DamageServerRpc(amount);
    }

    public void RequestRevive(float healthAmount)
    {
        if (IsServerInitialized)
            ApplyReviveServer(healthAmount);
        else
            ReviveServerRpc(healthAmount);
    }

    [ServerRpc(RequireOwnership = false)]
    void DamageServerRpc(float amount)
    {
        ApplyDamageServer(amount);
    }

    [ServerRpc(RequireOwnership = false)]
    void ReviveServerRpc(float healthAmount)
    {
        ApplyReviveServer(healthAmount);
    }

    void ApplyDamageServer(float amount)
    {
        if (syncedDead.Value || amount <= 0f)
            return;

        float next = Mathf.Max(0f, syncedHealth.Value - amount);
        syncedHealth.Value = next;
        if (next <= 0f)
            syncedDead.Value = true;
    }

    void ApplyReviveServer(float healthAmount)
    {
        if (!syncedDead.Value)
            return;

        syncedDead.Value = false;
        syncedHealth.Value = Mathf.Clamp(healthAmount, 1f, health.MaxHealth);
    }

    void OnSyncedHealthChanged(float prev, float next, bool asServer)
    {
        health.ApplyFullState(next, syncedDead.Value);
    }

    void OnSyncedDeadChanged(bool prev, bool next, bool asServer)
    {
        health.ApplyFullState(syncedHealth.Value, next);
    }
}
