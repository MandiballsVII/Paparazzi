using System;
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

        // Si hay componente de red, la autoridad la tiene el servidor.
        if (TryGetComponent(out NetworkPlayerHealth networkHealth) && networkHealth.IsSpawned)
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

        if (TryGetComponent(out NetworkPlayerHealth networkHealth) && networkHealth.IsSpawned)
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
}
