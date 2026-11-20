using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rejilla bajo trampilla: Interact saca al jugador a un suelo (layer Floor) adyacente seguro.
/// Online: teleport en el owner (NetworkTransform sincroniza). Local: directo.
/// </summary>
public class AirVentExit : MonoBehaviour, IPlayerInteractable
{
    [SerializeField]
    TrapController trap;

    [SerializeField]
    string prompt = "Pulsar interactuar para salir";

    [SerializeField]
    float interactDistance = 2.5f;

    [SerializeField]
    float feedbackSeconds = 2.5f;

    [SerializeField]
    Vector3 promptLocalOffset = new Vector3(0f, 0.35f, 0f);

    Text worldPrompt;
    Text feedbackText;
    float feedbackUntil;

    public string Prompt => prompt;
    public float InteractDistance => interactDistance;

    void Awake()
    {
        if (trap == null)
            trap = GetComponentInParent<TrapController>();

        if (trap == null)
            trap = FindNearestTrap();

        EnsureInteractCollider();
        EnsurePromptLabel();
    }

    void Update()
    {
        if (feedbackText == null)
            return;

        feedbackText.enabled = Time.time < feedbackUntil;
    }

    public bool CanInteract(GameObject interactor)
    {
        return interactor != null && isActiveAndEnabled && HasLocalAuthority(interactor);
    }

    public void Interact(GameObject interactor)
    {
        if (!HasLocalAuthority(interactor))
            return;

        if (trap == null)
            trap = GetComponentInParent<TrapController>() ?? FindNearestTrap();

        if (trap == null)
        {
            ShowFeedback("Trampa no encontrada.");
            return;
        }

        if (!trap.TryGetSafeExitWorldPosition(out Vector3 exitPos, out string side))
        {
            ShowFeedback("No hay salida segura al suelo.");
            Debug.LogWarning($"[AirVentExit:{name}] sin posición de salida (trap={trap.name}).", this);
            return;
        }

        if (!TryTeleport(interactor, exitPos))
        {
            ShowFeedback("No se pudo salir.");
            return;
        }

        Debug.Log($"[AirVentExit:{name}] '{interactor.name}' sale por lado {side} → {exitPos}", this);
    }

    static bool HasLocalAuthority(GameObject interactor)
    {
        if (interactor.TryGetComponent(out NetworkPlayerController netPlayer))
            return netPlayer.HasLocalControl;

        // Split-screen / jugador sin NetworkPlayerController.
        return interactor.GetComponentInParent<CharacterController>() != null;
    }

    static bool TryTeleport(GameObject interactor, Vector3 worldPosition)
    {
        if (interactor == null)
            return false;

        Transform root = interactor.transform;
        if (!interactor.TryGetComponent(out CharacterController controller))
            controller = interactor.GetComponentInParent<CharacterController>();

        if (controller != null)
        {
            root = controller.transform;
            controller.enabled = false;
            root.position = worldPosition;
            controller.enabled = true;
            return true;
        }

        root.position = worldPosition;
        return true;
    }

    TrapController FindNearestTrap()
    {
        TrapController[] traps = FindObjectsByType<TrapController>(FindObjectsSortMode.None);
        TrapController best = null;
        float bestSq = float.MaxValue;
        Vector3 p = transform.position;

        for (int i = 0; i < traps.Length; i++)
        {
            TrapController t = traps[i];
            if (t == null)
                continue;

            float sq = (t.transform.position - p).sqrMagnitude;
            if (sq >= bestSq)
                continue;

            bestSq = sq;
            best = t;
        }

        return best;
    }

    void EnsureInteractCollider()
    {
        // PlayerInteractUtility ignora triggers: hace falta collider sólido.
        if (TryGetComponent(out Collider col))
        {
            col.isTrigger = false;
            return;
        }

        Collider childCol = GetComponentInChildren<Collider>();
        if (childCol != null)
        {
            childCol.isTrigger = false;
            return;
        }

        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.size = Vector3.one;
    }

    void ShowFeedback(string message)
    {
        EnsurePromptLabel();
        if (feedbackText == null)
            return;

        feedbackText.text = message;
        feedbackUntil = Time.time + feedbackSeconds;
        Debug.Log($"[AirVentExit] {message}", this);
    }

    void EnsurePromptLabel()
    {
        if (worldPrompt != null)
            return;

        var canvasGo = new GameObject("VentPromptCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localPosition = promptLocalOffset;
        canvasGo.transform.localRotation = Quaternion.identity;
        canvasGo.transform.localScale = Vector3.one * 0.01f;

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.sizeDelta = new Vector2(420f, 120f);

        var promptGo = new GameObject("Prompt", typeof(Text));
        promptGo.transform.SetParent(canvasGo.transform, false);
        RectTransform promptRt = promptGo.GetComponent<RectTransform>();
        promptRt.anchorMin = new Vector2(0f, 0.45f);
        promptRt.anchorMax = Vector2.one;
        promptRt.offsetMin = Vector2.zero;
        promptRt.offsetMax = Vector2.zero;
        worldPrompt = promptGo.GetComponent<Text>();
        worldPrompt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (worldPrompt.font == null)
            worldPrompt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        worldPrompt.fontSize = 28;
        worldPrompt.alignment = TextAnchor.MiddleCenter;
        worldPrompt.color = Color.white;
        worldPrompt.text = prompt;

        var feedbackGo = new GameObject("Feedback", typeof(Text));
        feedbackGo.transform.SetParent(canvasGo.transform, false);
        RectTransform feedbackRt = feedbackGo.GetComponent<RectTransform>();
        feedbackRt.anchorMin = Vector2.zero;
        feedbackRt.anchorMax = new Vector2(1f, 0.45f);
        feedbackRt.offsetMin = Vector2.zero;
        feedbackRt.offsetMax = Vector2.zero;
        feedbackText = feedbackGo.GetComponent<Text>();
        feedbackText.font = worldPrompt.font;
        feedbackText.fontSize = 22;
        feedbackText.alignment = TextAnchor.MiddleCenter;
        feedbackText.color = new Color(1f, 0.75f, 0.35f);
        feedbackText.enabled = false;

        canvasGo.AddComponent<ExtractPromptBillboard>();
    }
}
