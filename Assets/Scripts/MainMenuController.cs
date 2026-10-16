using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    const string GameplaySceneName = "Gameplay";

    [SerializeField] InputField addressField;
    [SerializeField] Text statusText;
    [SerializeField] Text hintText;

    string address = "127.0.0.1";

    void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        address = NetworkAddressUtility.LoadLastJoinAddress("127.0.0.1");

        if (addressField == null || statusText == null)
            BuildUi();
        else
            WireExistingUi();
    }

    void WireExistingUi()
    {
        addressField.text = address;
        addressField.onValueChanged.AddListener(value => address = value);
        SetStatus("Elige un modo para empezar.");
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            DontDestroyOnLoad(eventSystem);
        }

        CreateLabel(canvasGo.transform, "Co-op Prototype", new Vector2(0f, 320f), 48);
        statusText = CreateLabel(canvasGo.transform, "Elige un modo para empezar.", new Vector2(0f, 250f), 22);

        CreateButton(canvasGo.transform, "Local (pantalla partida)", new Vector2(0f, 140f), OnLocalClicked, new Vector2(460f, 56f));
        CreateButton(canvasGo.transform, "Host (abre partida)", new Vector2(0f, 70f), OnHostClicked, new Vector2(460f, 56f));
        CreateButton(canvasGo.transform, "Unirse (Join)", new Vector2(0f, 0f), OnJoinClicked, new Vector2(460f, 56f));

        addressField = CreateAddressField(canvasGo.transform, new Vector2(0f, -70f));

        CreateButton(canvasGo.transform, "IP: mismo PC", new Vector2(-120f, -140f), OnFillLocalhostClicked, new Vector2(200f, 44f));
        CreateButton(canvasGo.transform, "IP: LAN (pegar host)", new Vector2(120f, -140f), OnFillLanHintClicked, new Vector2(220f, 44f));

        hintText = CreateLabel(canvasGo.transform, BuildHintText(), new Vector2(0f, -220f), 16);
        RectTransform hintRect = hintText.rectTransform;
        hintRect.sizeDelta = new Vector2(1100f, 90f);
    }

    string BuildHintText()
    {
        string localIps = NetworkAddressUtility.FormatLocalAddressesForDisplay();
        return
            "Join usa la IP del campo. Mismo PC: 127.0.0.1  ·  Portátil→sobremesa: IP LAN del host.\n" +
            localIps + "\n" +
            "Firewall: permitir Unity/juego en redes Privadas, UDP puerto " + GameSessionConfig.DefaultPort + ".";
    }

    Text CreateLabel(Transform parent, string text, Vector2 anchoredPos, int fontSize)
    {
        var go = new GameObject("Label", typeof(Text));
        go.transform.SetParent(parent, false);
        Text label = go.GetComponent<Text>();
        label.text = text;
        label.alignment = TextAnchor.MiddleCenter;
        label.font = GetUiFont();
        label.fontSize = fontSize;
        label.color = Color.white;

        RectTransform rect = label.rectTransform;
        rect.sizeDelta = new Vector2(900f, 60f);
        rect.anchoredPosition = anchoredPos;
        return label;
    }

    void CreateButton(Transform parent, string label, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick, Vector2 size)
    {
        var go = new GameObject(label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.15f, 0.15f, 0.18f, 0.95f);

        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPos;

        var textGo = new GameObject("Text", typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        Text text = textGo.GetComponent<Text>();
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.font = GetUiFont();
        text.fontSize = size.y >= 56f ? 24 : 18;
        text.color = Color.white;

        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }

    InputField CreateAddressField(Transform parent, Vector2 anchoredPos)
    {
        var go = new GameObject("AddressField", typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.08f, 0.08f, 0.1f, 0.95f);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(460f, 48f);
        rect.anchoredPosition = anchoredPos;

        var textGo = new GameObject("Text", typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        Text text = textGo.GetComponent<Text>();
        text.font = GetUiFont();
        text.fontSize = 22;
        text.color = Color.white;
        text.supportRichText = false;

        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 6f);
        textRect.offsetMax = new Vector2(-12f, -6f);

        var placeholderGo = new GameObject("Placeholder", typeof(Text));
        placeholderGo.transform.SetParent(go.transform, false);
        Text placeholder = placeholderGo.GetComponent<Text>();
        placeholder.font = text.font;
        placeholder.fontSize = 22;
        placeholder.fontStyle = FontStyle.Italic;
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        placeholder.text = "IP del host (ej. 127.0.0.1 o 192.168.0.13)";

        RectTransform placeholderRect = placeholder.rectTransform;
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(12f, 6f);
        placeholderRect.offsetMax = new Vector2(-12f, -6f);

        InputField field = go.GetComponent<InputField>();
        field.textComponent = text;
        field.placeholder = placeholder;
        field.text = address;
        field.contentType = InputField.ContentType.Standard;
        field.onValueChanged.AddListener(value => address = value);
        return field;
    }

    static Font GetUiFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
            return font;

        return Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 16);
    }

    void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    void SetAddress(string value)
    {
        address = value;
        if (addressField != null)
            addressField.text = value;
    }

    public void OnFillLocalhostClicked()
    {
        SetAddress("127.0.0.1");
        SetStatus("IP lista para mismo PC (MPPM / Editor+Build). Pulsa Unirse.");
    }

    public void OnFillLanHintClicked()
    {
        var locals = NetworkAddressUtility.GetLocalIPv4Addresses();
        if (locals.Count > 0)
        {
            // En el cliente LAN normalmente pegas la IP del HOST, no la tuya.
            // Aquí rellenamos la primera IP local solo como referencia / si este PC es el host.
            SetAddress(locals[0]);
            SetStatus($"IP local detectada: {locals[0]}. En el PORTÁTIL escribe la IP del SOBREMESA (host), no esta.");
        }
        else
        {
            SetAddress("192.168.0.13");
            SetStatus("No se detectó IP. Campo puesto a 192.168.0.13 (tu sobremesa). Edítalo si cambió.");
        }
    }

    public void OnLocalClicked()
    {
        GameSessionConfig.SetLocal();
        SetStatus("Cargando modo local...");
        SceneManager.LoadScene(GameplaySceneName);
    }

    public void OnHostClicked()
    {
        GameSessionConfig.SetHost();
        string lanInfo = NetworkAddressUtility.FormatLocalAddressesForDisplay();
        SetStatus("Cargando host... " + lanInfo);
        Debug.Log(lanInfo);
        SceneManager.LoadScene(GameplaySceneName);
    }

    public void OnJoinClicked()
    {
        if (addressField != null)
            address = addressField.text;

        NetworkAddressUtility.SaveLastJoinAddress(address);
        GameSessionConfig.SetClient(address);
        SetStatus($"Conectando a {GameSessionConfig.JoinAddress}:{GameSessionConfig.DefaultPort}...");
        SceneManager.LoadScene(GameplaySceneName);
    }
}
