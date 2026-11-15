using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Arranque fiable del Lobby: UI de resultado + colocación de jugadores preservados.
/// </summary>
public class LobbyBootstrap : MonoBehaviour
{
    const string MainMenuSceneName = "MainMenu";
    const string LobbySceneName = "Lobby";

    Text messageText;
    bool uiBuilt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, LobbySceneName, System.StringComparison.Ordinal))
            return;

        LobbyBootstrap existing = Object.FindAnyObjectByType<LobbyBootstrap>();
        if (existing != null)
        {
            existing.ActivateLobby();
            return;
        }

        var go = new GameObject("LobbyBootstrap");
        LobbyBootstrap bootstrap = go.AddComponent<LobbyBootstrap>();
        bootstrap.ActivateLobby();
    }

    void ActivateLobby()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        DisableSceneOnlyCameras();
        Vector3 center = FindLobbyPlaneCenter();
        LobbyPlayerTransfer.PlacePreservedPlayersInLobby(center);
        EnsureResultUi();
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
            GoMainMenu();
    }

    static Vector3 FindLobbyPlaneCenter()
    {
        MeshFilter[] filters = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i] == null)
                continue;
            if (filters[i].sharedMesh != null && filters[i].sharedMesh.name.Contains("Plane"))
                return filters[i].transform.position;
            if (filters[i].name.IndexOf("Plane", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return filters[i].transform.position;
        }

        GameObject plane = GameObject.Find("Plane");
        if (plane != null)
            return plane.transform.position;

        return Vector3.zero;
    }

    static void DisableSceneOnlyCameras()
    {
        Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cams.Length; i++)
        {
            Camera cam = cams[i];
            if (cam == null)
                continue;
            // No tocar cámaras de jugadores preservados.
            if (cam.GetComponentInParent<PlayerSessionIdentity>() != null)
                continue;
            if (cam.GetComponentInParent<PlayerHealth>() != null)
                continue;

            cam.enabled = false;
            if (cam.TryGetComponent(out AudioListener listener))
                listener.enabled = false;
        }
    }

    void EnsureResultUi()
    {
        if (uiBuilt)
            return;
        uiBuilt = true;

        var canvasGo = new GameObject("LobbyResultCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var panel = new GameObject("Banner", typeof(Image));
        panel.transform.SetParent(canvasGo.transform, false);
        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.15f, 0.72f);
        panelRt.anchorMax = new Vector2(0.85f, 0.95f);
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        var textGo = new GameObject("ResultText", typeof(Text));
        textGo.transform.SetParent(panel.transform, false);
        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(16f, 8f);
        textRt.offsetMax = new Vector2(-16f, -8f);

        messageText = textGo.GetComponent<Text>();
        messageText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (messageText.font == null)
            messageText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        messageText.fontSize = 36;
        messageText.alignment = TextAnchor.MiddleCenter;
        messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
        messageText.verticalOverflow = VerticalWrapMode.Overflow;

        if (RoundResultStore.HasResult)
        {
            messageText.text = RoundResultStore.Message + "\n(Enter / Espacio / Esc → Menú)";
            messageText.color = RoundResultStore.IsVictory
                ? new Color(0.45f, 0.95f, 0.55f)
                : new Color(0.95f, 0.75f, 0.35f);
        }
        else
        {
            messageText.text = "Lobby\n(Enter / Espacio / Esc → Menú)";
            messageText.color = Color.white;
        }
    }

    void GoMainMenu()
    {
        LobbyPlayerTransfer.DestroyPreservedPlayers();
        RoundResultStore.Clear();
        RoundObjectiveState.Clear();
        GameSessionConfig.Clear();
        SceneManager.LoadScene(MainMenuSceneName);
    }
}
