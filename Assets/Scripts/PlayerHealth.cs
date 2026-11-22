using System;
using FishNet.Object;
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField] float maxHealth = 100f;
    [SerializeField] float reviveHealth = 10f;
    [SerializeField] float testDamageAmount = 30f;

    float currentHealth;
    bool isDead;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float ReviveHealthAmount => reviveHealth;
    public float TestDamageAmount => testDamageAmount;
    public bool IsDead => isDead;

    public event Action<float, float> HealthChanged;
    public event Action Died;
    public event Action Revived;

    void Awake()
    {
        currentHealth = maxHealth;
        isDead = false;
    }

    void Start()
    {
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(float amount)
    {
        if (isDead || amount <= 0f)
            return;

        if (TryGetNetworkHealth(out NetworkPlayerHealth networkHealth))
        {
            networkHealth.RequestDamage(amount);
            return;
        }

        ApplyDamage(amount);
    }

    public void ApplyDamage(float amount)
    {
        if (isDead || amount <= 0f)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        HealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0f)
            Die();
    }

    public void Revive()
    {
        Revive(reviveHealth);
    }

    public void Revive(float healthAmount)
    {
        if (!isDead)
            return;

        if (TryGetNetworkHealth(out NetworkPlayerHealth networkHealth))
        {
            networkHealth.RequestRevive(healthAmount);
            return;
        }

        ApplyRevive(healthAmount);
    }

    public void ApplyRevive(float healthAmount)
    {
        if (!isDead)
            return;

        isDead = false;
        currentHealth = Mathf.Clamp(healthAmount, 1f, maxHealth);
        HealthChanged?.Invoke(currentHealth, maxHealth);
        Revived?.Invoke();
    }

    public void ApplyFullState(float health, bool dead)
    {
        currentHealth = Mathf.Clamp(health, 0f, maxHealth);
        bool wasDead = isDead;
        isDead = dead;
        HealthChanged?.Invoke(currentHealth, maxHealth);

        if (isDead && !wasDead)
            Died?.Invoke();
        else if (!isDead && wasDead)
            Revived?.Invoke();
    }

    void Die()
    {
        if (isDead)
            return;

        ApplyFullState(0f, true);
    }

    public void DealTestDamage()
    {
        TakeDamage(testDamageAmount);
    }

    /// <summary>
    /// Solo usa red si NetworkPlayerHealth está en el prefab (spawneado) y el NetworkObject es válido.
    /// </summary>
    bool TryGetNetworkHealth(out NetworkPlayerHealth networkHealth)
    {
        networkHealth = null;
        if (!TryGetComponent(out networkHealth) || networkHealth == null)
            return false;

        // Nunca tocar NetworkBehaviour.IsSpawned si el NB se añadió en runtime (cache nula).
        if (!TryGetComponent(out NetworkObject nob) || nob == null || !nob.IsSpawned)
            return false;

        return true;
    }
}
