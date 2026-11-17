using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Botón de la furgoneta: Interact finaliza la ronda si todos los vivos están dentro.
/// </summary>
public class RoundExtractButton : MonoBehaviour, IPlayerInteractable
{
    [SerializeField]
    VehicleInteriorZone interiorZone;

    [SerializeField]
    string prompt = "Pulsar para finalizar la ronda";

    [SerializeField]
    float interactDistance = 3f;

    [SerializeField]
    float feedbackSeconds = 2.5f;

    Text worldPrompt;
    Text feedbackText;
    float feedbackUntil;

    public string Prompt => prompt;
    public float InteractDistance => interactDistance;

    void Awake()
    {
        if (interiorZone == null)
            interiorZone = GetComponentInParent<VehicleInteriorZone>();
        if (interiorZone == null)
        {
            Transform root = transform.root;
            if (root != null)
                interiorZone = root.GetComponentInChildren<VehicleInteriorZone>(true);
        }

        EnsurePromptLabel();
    }

    public void SetInteriorZone(VehicleInteriorZone zone)
    {
        interiorZone = zone;
    }

    void Update()
    {
        if (feedbackText == null)
            return;

        if (Time.time < feedbackUntil)
        {
            feedbackText.enabled = true;
            return;
        }

        feedbackText.enabled = false;
    }

    public bool CanInteract(GameObject interactor)
    {
        return interactor != null && isActiveAndEnabled;
    }

    public void Interact(GameObject interactor)
    {
        NetworkRoundExtract extract = NetworkRoundExtract.Instance;
        if (extract == null)
            extract = FindAnyObjectByType<NetworkRoundExtract>();

        if (extract != null)
        {
            extract.RequestExtractFrom(interactor, out string failReason);
            // Online client: el fallo/éxito vuelve por ObserversRpc; failReason solo en host local.
            if (!string.IsNullOrEmpty(failReason))
                ShowFeedback(failReason);
            return;
        }

        // Fallback local sin NetworkRoundExtract.
        if (!TryValidateExtractionLocal(out string localFail, out bool victory))
        {
            ShowFeedback(localFail);
            return;
        }

        RoundResultStore.SetExtractResult(victory);
        MatchSessionExit.ExtractToLobby();
    }

    bool TryValidateExtractionLocal(out string failReason, out bool victory)
    {
        failReason = string.Empty;
        victory = false;

        if (interiorZone == null)
        {
            failReason = "Zona de la furgoneta no encontrada.";
            return false;
        }

        IReadOnlyList<PlayerHealth> inVan = interiorZone.GetLivingOccupants();
        PlayerHealth[] all = Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        int livingTotal = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && !all[i].IsDead)
                livingTotal++;
        }

        if (livingTotal <= 0)
        {
            failReason = "No hay jugadores vivos.";
            return false;
        }

        if (inVan.Count < livingTotal)
        {
            failReason = $"Faltan jugadores vivos en la furgoneta ({inVan.Count}/{livingTotal}).";
            return false;
        }

        victory = RoundObjectiveState.HasVictoryPhotoOwnedByOccupant(inVan);
        return true;
    }

    public void ShowFeedbackPublic(string message) => ShowFeedback(message);

    void ShowFeedback(string message)
    {
        EnsurePromptLabel();
        if (feedbackText == null)
            return;

        feedbackText.text = message;
        feedbackUntil = Time.time + feedbackSeconds;
        Debug.Log($"[RoundExtract] {message}", this);
    }

    void EnsurePromptLabel()
    {
        if (worldPrompt != null)
            return;

        var canvasGo = new GameObject("ExtractPromptCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
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

        // Billboarding simple hacia la cámara principal / primera activa.
        canvasGo.AddComponent<ExtractPromptBillboard>();
    }
}

public class ExtractPromptBillboard : MonoBehaviour
{
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                if (cams[i] != null && cams[i].enabled)
                {
                    cam = cams[i];
                    break;
                }
            }
        }

        if (cam == null)
            return;

        Vector3 toCam = transform.position - cam.transform.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 0.001f)
            return;
        transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
    }
}
