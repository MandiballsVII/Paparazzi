using UnityEngine;

[RequireComponent(typeof(PlayerHealth))]
public class PlayerHealthHud : MonoBehaviour
{
    [SerializeField] Camera playerCamera;

    PlayerHealth health;
    string label = "100/100";

    public void Initialize(Camera camera)
    {
        playerCamera = camera;
    }

    void Awake()
    {
        health = GetComponent<PlayerHealth>();

        if (playerCamera == null)
        {
            Transform pivot = transform.Find("CameraPivot");
            if (pivot != null)
                playerCamera = pivot.GetComponentInChildren<Camera>(true);
        }
    }

    void OnEnable()
    {
        health.HealthChanged += OnHealthChanged;
        OnHealthChanged(health.CurrentHealth, health.MaxHealth);
    }

    void OnDisable()
    {
        health.HealthChanged -= OnHealthChanged;
    }

    void OnHealthChanged(float current, float max)
    {
        label = $"{Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}";
    }

    void OnGUI()
    {
        if (playerCamera == null || !playerCamera.enabled)
            return;

        // Solo el dueño local / cámara activa dibuja su HUD (split-screen por pixelRect).
        Rect pixel = playerCamera.pixelRect;
        float x = pixel.x + 12f;
        float y = Screen.height - pixel.yMax + 12f;

        GUI.color = health.IsDead ? new Color(1f, 0.35f, 0.35f) : Color.white;
        GUI.Label(new Rect(x, y, 220f, 28f), $"VIDA  {label}");

        if (health.IsDead)
        {
            GUI.Label(new Rect(x, y + 24f, 320f, 24f), "MUERTO — llévalo al vehículo para revivir");
            return;
        }

        if (GUI.Button(new Rect(x, y + 28f, 160f, 28f), $"Daño test (-{health.TestDamageAmount:0})"))
            health.DealTestDamage();
    }
}
