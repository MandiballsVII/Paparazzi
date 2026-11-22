using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerHealth))]
public class PlayerHealthHud : MonoBehaviour
{
    [SerializeField]
    Camera playerCamera;

    [SerializeField]
    GameObject hudRoot;

    [SerializeField]
    Canvas hudCanvas;

    [SerializeField]
    TMP_Text healthText;

    [SerializeField]
    TMP_Text deadText;

    [SerializeField]
    Button testDamageButton;

    [SerializeField]
    TMP_Text testDamageButtonLabel;

    [Tooltip("Obsoleto en HUD: el test de daño está en el menú de pausa.")]
    [SerializeField]
    bool showTestDamageButton = false;

    PlayerHealth health;
    bool buttonWired;
    bool localHudAllowed = true;

    public void Initialize(Camera camera)
    {
        playerCamera = camera;
        EnsureHud();
        ApplyCanvasCamera();
        ApplyTestDamageVisibility();
        RefreshLocalVisibility();
        RefreshAll();
    }

    /// <summary>Online: solo el owner / control local debe ver la HUD.</summary>
    public void SetLocalHudAllowed(bool allowed)
    {
        localHudAllowed = allowed;
        RefreshLocalVisibility();
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

        EnsureHud();
        ApplyCanvasCamera();
        ApplyTestDamageVisibility();
    }

    void OnEnable()
    {
        if (health == null)
            health = GetComponent<PlayerHealth>();

        if (health != null)
        {
            health.HealthChanged += OnHealthChanged;
            health.Died += OnDiedOrRevived;
            health.Revived += OnDiedOrRevived;
        }

        WireTestDamageButton();
        RefreshAll();
        RefreshLocalVisibility();
    }

    void OnDisable()
    {
        if (health != null)
        {
            health.HealthChanged -= OnHealthChanged;
            health.Died -= OnDiedOrRevived;
            health.Revived -= OnDiedOrRevived;
        }

        UnwireTestDamageButton();
    }

    void OnValidate()
    {
        ApplyTestDamageVisibility();
    }

    void OnHealthChanged(float current, float max)
    {
        RefreshHealthLabel(current, max);
        RefreshDeadState();
    }

    void OnDiedOrRevived()
    {
        RefreshDeadState();
        ApplyTestDamageVisibility();
    }

    void RefreshAll()
    {
        if (health == null)
            return;

        RefreshHealthLabel(health.CurrentHealth, health.MaxHealth);
        RefreshDeadState();
        ApplyTestDamageVisibility();
    }

    void RefreshHealthLabel(float current, float max)
    {
        if (healthText == null)
            return;

        healthText.text = $"{Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}";
        healthText.color = health != null && health.IsDead
            ? new Color(1f, 0.35f, 0.35f)
            : Color.white;
    }

    void RefreshDeadState()
    {
        if (deadText == null)
            return;

        bool dead = health != null && health.IsDead;
        deadText.gameObject.SetActive(dead);
        if (dead)
            deadText.text = "MUERTO — llévalo al vehículo para revivir";
    }

    void ApplyTestDamageVisibility()
    {
        if (testDamageButton == null)
            return;

        bool show = showTestDamageButton && (health == null || !health.IsDead);
        testDamageButton.gameObject.SetActive(show);

        if (testDamageButtonLabel != null && health != null)
            testDamageButtonLabel.text = $"Daño test (-{health.TestDamageAmount:0})";
    }

    void WireTestDamageButton()
    {
        if (buttonWired || testDamageButton == null)
            return;

        testDamageButton.onClick.AddListener(OnTestDamageClicked);
        buttonWired = true;
    }

    void UnwireTestDamageButton()
    {
        if (!buttonWired || testDamageButton == null)
            return;

        testDamageButton.onClick.RemoveListener(OnTestDamageClicked);
        buttonWired = false;
    }

    void OnTestDamageClicked()
    {
        if (health != null)
            health.DealTestDamage();
    }

    void ApplyCanvasCamera()
    {
        if (hudCanvas == null || playerCamera == null)
            return;

        hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        hudCanvas.worldCamera = playerCamera;
        hudCanvas.planeDistance = 0.2f;
    }

    void RefreshLocalVisibility()
    {
        if (hudRoot == null)
            return;

        bool camOk = playerCamera != null && playerCamera.enabled;
        hudRoot.SetActive(localHudAllowed && camOk);
    }

    /// <summary>
    /// Si el prefab aún no tiene UI asignada, crea una HUD mínima bajo la cámara.
    /// Preferible: generar/editar la jerarquía en el prefab (Tools → Paparazzi).
    /// </summary>
    void EnsureHud()
    {
        if (healthText != null && hudRoot != null)
            return;

        if (playerCamera == null)
            return;

        Transform camT = playerCamera.transform;
        Transform existing = camT.Find("PlayerHud");
        if (existing != null)
        {
            BindExistingHud(existing.gameObject);
            return;
        }

        var root = new GameObject("PlayerHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(camT, false);

        hudRoot = root;
        hudCanvas = root.GetComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        hudCanvas.worldCamera = playerCamera;
        hudCanvas.planeDistance = 0.2f;
        hudCanvas.sortingOrder = 50;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rootRt = root.GetComponent<RectTransform>();
        StretchFull(rootRt);

        healthText = CreateTmp(root.transform, "HealthText", new Vector2(24f, -24f), new Vector2(420f, 48f), 36, TextAlignmentOptions.TopLeft);
        healthText.text = "100/100";

        deadText = CreateTmp(root.transform, "DeadText", new Vector2(24f, -72f), new Vector2(720f, 40f), 28, TextAlignmentOptions.TopLeft);
        deadText.gameObject.SetActive(false);

        // Botón de daño test: solo en menú de pausa (no se crea en HUD).
    }

    void BindExistingHud(GameObject root)
    {
        hudRoot = root;
        root.TryGetComponent(out hudCanvas);

        Transform healthT = root.transform.Find("HealthText");
        if (healthT != null)
            healthT.TryGetComponent(out healthText);

        Transform deadT = root.transform.Find("DeadText");
        if (deadT != null)
            deadT.TryGetComponent(out deadText);

        Transform buttonT = root.transform.Find("TestDamageButton");
        if (buttonT != null)
        {
            buttonT.TryGetComponent(out testDamageButton);
            Transform labelT = buttonT.Find("Label");
            if (labelT != null)
                labelT.TryGetComponent(out testDamageButtonLabel);
        }
    }

    static TMP_Text CreateTmp(
        Transform parent,
        string objectName,
        Vector2 anchoredPos,
        Vector2 size,
        float fontSize,
        TextAlignmentOptions align)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }

    static Button CreateButton(
        Transform parent,
        string objectName,
        Vector2 anchoredPos,
        Vector2 size,
        out TMP_Text label)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.15f, 0.15f, 0.18f, 0.85f);

        Button button = go.GetComponent<Button>();

        label = CreateTmp(go.transform, "Label", Vector2.zero, size, 24, TextAlignmentOptions.Center);
        RectTransform labelRt = label.rectTransform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.pivot = new Vector2(0.5f, 0.5f);
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        label.raycastTarget = false;

        return button;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }
}
