using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(PlayerPhotoAlbum))]
public class PlayerPauseMenu : MonoBehaviour
{
    const int ThumbColumns = 4;
    const int ThumbRows = 3;
    const int VisibleThumbSlots = ThumbColumns * ThumbRows;

    [SerializeField] Camera playerCamera;

    [Tooltip("Botones de prueba en el menú de pausa (clicables con cursor).")]
    [SerializeField] bool showTestDamageButton = true;

    [SerializeField] bool showTestAlarmButton = true;

    PlayerInput playerInput;
    PlayerPhotoAlbum album;
    PlayerHealth health;
    InputAction pauseAction;
    InputAction cancelAction;
    bool pauseBound;
    bool cancelBound;
    bool isOpen;
    bool showingPhotos;

    Canvas canvas;
    RectTransform canvasRect;
    GameObject rootPanel;
    RectTransform rootRect;
    GameObject mainPanel;
    GameObject photosPanel;
    RectTransform photosContent;
    RectTransform photosScrollViewport;
    GridLayoutGroup photosGrid;
    readonly List<RawImage> thumbSlots = new List<RawImage>();
    Button photosButton;
    Button testDamageButton;
    Button testAlarmButton;
    MultiplayerEventSystem multiplayerEventSystem;
    InputSystemUIInputModule uiModule;
    GraphicRaycaster graphicRaycaster;

    public bool IsOpen => isOpen;

    public void Initialize(Camera camera)
    {
        if (playerInput == null)
            TryGetComponent(out playerInput);
        if (album == null)
            TryGetComponent(out album);

        playerCamera = camera;
        EnsureUi();
        ConfigureUiModule();
        FitRootToCameraViewport();
    }

    void LateUpdate()
    {
        if (canvas == null || playerCamera == null)
            return;

        FitRootToCameraViewport();
    }

    void Update()
    {
        if (!isOpen)
            return;

        // Solo asegurar cursor libre. El clic lo gestiona EventSystem/uGUI;
        // un Invoke manual aquí duplicaba cada pulsación (fallback + OnPointerClick).
        EnsureMouseCursorForUi();
    }

    void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        album = GetComponent<PlayerPhotoAlbum>();
        TryGetComponent(out health);

        if (playerCamera == null)
        {
            Transform pivot = transform.Find("CameraPivot");
            if (pivot != null)
                playerCamera = pivot.GetComponentInChildren<Camera>(true);
        }
    }

    void OnEnable()
    {
        album.PhotosChanged += RefreshPhotoGrid;
        BindPause();
    }

    void OnDisable()
    {
        album.PhotosChanged -= RefreshPhotoGrid;
        UnbindPause();
        UnbindCancel();
        if (isOpen)
            ForceCloseWithoutInputRestore();
    }

    public void BindPause()
    {
        if (pauseBound || playerInput == null || playerInput.actions == null)
            return;

        pauseAction = playerInput.actions["Pause"];
        if (pauseAction == null)
            return;

        pauseAction.performed += OnPausePerformed;
        pauseBound = true;
    }

    public void UnbindPause()
    {
        if (!pauseBound || pauseAction == null)
            return;

        pauseAction.performed -= OnPausePerformed;
        pauseBound = false;
    }

    void BindCancel()
    {
        if (cancelBound || playerInput == null || playerInput.actions == null)
            return;

        cancelAction = playerInput.actions["Cancel"];
        if (cancelAction == null)
            return;

        cancelAction.performed += OnCancelPerformed;
        cancelBound = true;
    }

    void UnbindCancel()
    {
        if (!cancelBound || cancelAction == null)
            return;

        cancelAction.performed -= OnCancelPerformed;
        cancelBound = false;
    }

    void OnPausePerformed(InputAction.CallbackContext context)
    {
        if (MatchSessionExit.IsExtractInProgress)
            return;

        if (!IsContextFromThisPlayer(context))
            return;

        if (!CanSwitchActionMaps())
            return;

        if (isOpen)
            CloseMenu();
        else
            OpenMenu();
    }

    void OnCancelPerformed(InputAction.CallbackContext context)
    {
        if (!isOpen)
            return;

        if (!IsContextFromThisPlayer(context))
            return;

        if (MatchSessionExit.IsExtractInProgress)
        {
            ForceCloseWithoutInputRestore();
            UnbindCancel();
            return;
        }

        // Solo fotos → principal. Cerrar pausa lo hace Pause (Esc/Start).
        // Si Cancel también cerrara el principal, Esc dispararía Cancel+Pause
        // (cierra y reabre en el mismo frame).
        if (showingPhotos)
            ShowMainPanel();
    }

    bool IsContextFromThisPlayer(InputAction.CallbackContext context)
    {
        // Online: PlayerInput suele no tener devices emparejados → no filtrar.
        // Local/Gym: solo aceptar input de los devices de este jugador.
        if (playerInput == null || playerInput.devices.Count == 0)
            return true;

        InputDevice device = context.control != null ? context.control.device : null;
        return PlayerInputUtility.IsDevicePairedToPlayer(playerInput, device);
    }

    /// <summary>
    /// Tras clonar el InputActionAsset hay que volver a enganchar callbacks.
    /// </summary>
    public void RebindActionsForPrivateAsset()
    {
        UnbindPause();
        UnbindCancel();
        BindPause();
        if (isOpen)
            BindCancel();
    }

    public void OpenMenu()
    {
        if (isOpen)
            return;

        if (MatchSessionExit.IsExtractInProgress || !CanSwitchActionMaps())
            return;

        EnsureUi();
        FitRootToCameraViewport();
        isOpen = true;
        showingPhotos = false;
        rootPanel.SetActive(true);
        ShowMainPanel();

        playerInput.SwitchCurrentActionMap("UI");
        // Mantener Pause activo para cerrar con Esc / Start aunque el mapa Player esté off.
        InputAction pause = playerInput.actions.FindAction("Pause", false);
        pause?.Enable();

        ConfigureUiModule();

        BindCancel();
        RefreshCursorForMenu(true);
        SelectButton(photosButton);
    }

    public void CloseMenu()
    {
        if (!isOpen)
            return;

        isOpen = false;
        showingPhotos = false;
        if (rootPanel != null)
            rootPanel.SetActive(false);

        UnbindCancel();

        if (CanSwitchActionMaps())
            playerInput.SwitchCurrentActionMap("Player");

        RefreshCursorForMenu(false);
    }

    bool CanSwitchActionMaps()
    {
        return playerInput != null
               && playerInput.enabled
               && playerInput.actions != null
               && playerInput.gameObject.activeInHierarchy;
    }

    void ForceCloseWithoutInputRestore()
    {
        isOpen = false;
        showingPhotos = false;
        if (rootPanel != null)
            rootPanel.SetActive(false);
    }

    void ShowMainPanel()
    {
        showingPhotos = false;
        mainPanel.SetActive(true);
        photosPanel.SetActive(false);
        SelectButton(photosButton);
    }

    void ShowPhotosPanel()
    {
        showingPhotos = true;
        mainPanel.SetActive(false);
        photosPanel.SetActive(true);
        FitRootToCameraViewport();
        RefreshPhotoGrid();
        SelectFirstPhotoOrBack();
    }

    void OnPhotosClicked() => ShowPhotosPanel();
    void OnResumeClicked() => CloseMenu();
    void OnLeaveClicked()
    {
        // Asegura limpiar jugadores DontDestroyOnLoad al salir al menú.
        MatchSessionExit.LeaveMatch();
    }
    void OnPhotosBackClicked() => ShowMainPanel();

    void RefreshCursorForMenu(bool menuOpen)
    {
        bool keyboardMouse =
            playerInput != null
            && (playerInput.currentControlScheme == "Keyboard&Mouse"
                || playerInput.currentControlScheme == null);

        if (!keyboardMouse)
            return;

        Cursor.lockState = menuOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = menuOpen;
    }

    void SelectButton(Button button)
    {
        if (button == null || multiplayerEventSystem == null)
            return;

        multiplayerEventSystem.SetSelectedGameObject(button.gameObject);
    }

    void SelectFirstPhotoOrBack()
    {
        if (photosPanel == null)
            return;

        Button back = photosPanel.GetComponentInChildren<Button>();
        // Prefer first interactive thumb if any later; for now select back at bottom via tagged find.
        Transform backTransform = photosPanel.transform.Find("BackButton");
        if (backTransform != null && backTransform.TryGetComponent(out Button backButton))
        {
            SelectButton(backButton);
            return;
        }

        if (back != null)
            SelectButton(back);
    }

    void EnsureUi()
    {
        if (canvas != null)
            return;

        if (playerCamera == null)
        {
            Transform pivot = transform.Find("CameraPivot");
            if (pivot != null)
                playerCamera = pivot.GetComponentInChildren<Camera>(true);
        }

        // Reusar canvas huérfano (p.ej. clon de template) en lugar de duplicar.
        Transform existing = transform.Find("PauseMenuCanvas");
        if (existing != null && existing.TryGetComponent(out Canvas existingCanvas))
        {
            BindExistingPauseCanvas(existing.gameObject, existingCanvas);
            if (canvas != null)
            {
                FitRootToCameraViewport();
                return;
            }
        }

        var canvasGo = new GameObject("PauseMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        graphicRaycaster = canvasGo.GetComponent<GraphicRaycaster>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasRect = canvasGo.GetComponent<RectTransform>();

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        EnsureEventSystem(canvasGo.transform);

        rootPanel = new GameObject("Root", typeof(Image));
        rootPanel.transform.SetParent(canvasGo.transform, false);
        Image dim = rootPanel.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.65f);
        dim.raycastTarget = false;
        rootRect = rootPanel.GetComponent<RectTransform>();
        StretchFull(rootRect);

        mainPanel = BuildMainPanel(rootPanel.transform);
        photosPanel = BuildPhotosPanel(rootPanel.transform);
        photosPanel.SetActive(false);
        rootPanel.SetActive(false);

        FitRootToCameraViewport();
    }

    void BindExistingPauseCanvas(GameObject canvasGo, Canvas existingCanvas)
    {
        canvas = existingCanvas;
        canvasGo.TryGetComponent(out graphicRaycaster);
        canvasRect = canvasGo.GetComponent<RectTransform>();

        Transform rootT = canvasGo.transform.Find("Root");
        if (rootT == null)
        {
            // Canvas vacío/roto: destruir y dejar que el caller cree uno limpio en la próxima pasada.
            Object.Destroy(canvasGo);
            canvas = null;
            return;
        }

        rootPanel = rootT.gameObject;
        rootRect = rootT.GetComponent<RectTransform>();
        Transform mainT = rootT.Find("MainPanel");
        Transform photosT = rootT.Find("PhotosPanel");
        mainPanel = mainT != null ? mainT.gameObject : null;
        photosPanel = photosT != null ? photosT.gameObject : null;

        if (mainPanel == null || photosPanel == null)
        {
            Object.Destroy(canvasGo);
            canvas = null;
            rootPanel = null;
            mainPanel = null;
            photosPanel = null;
            return;
        }

        if (canvasGo.transform.Find("PlayerEventSystem") == null)
            EnsureEventSystem(canvasGo.transform);
        else
        {
            Transform es = canvasGo.transform.Find("PlayerEventSystem");
            if (es != null)
            {
                multiplayerEventSystem = es.GetComponent<MultiplayerEventSystem>();
                uiModule = es.GetComponent<InputSystemUIInputModule>();
                ConfigureUiModule();
            }
        }

        photosButton = mainPanel.GetComponentInChildren<Button>(true);
        testDamageButton = FindNamedButton(mainPanel.transform, "TestDamageButton");
        testAlarmButton = FindNamedButton(mainPanel.transform, "TestAlarmButton");
    }

    static Button FindNamedButton(Transform root, string objectName)
    {
        Transform t = root.Find(objectName);
        if (t != null && t.TryGetComponent(out Button button))
            return button;
        return null;
    }

    void FitRootToCameraViewport()
    {
        if (rootRect == null || playerCamera == null)
            return;

        Rect pixel = playerCamera.pixelRect;
        float screenW = Mathf.Max(1f, Screen.width);
        float screenH = Mathf.Max(1f, Screen.height);

        rootRect.anchorMin = new Vector2(pixel.xMin / screenW, pixel.yMin / screenH);
        rootRect.anchorMax = new Vector2(pixel.xMax / screenW, pixel.yMax / screenH);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
    }

    void EnsureEventSystem(Transform canvasTransform)
    {
        var eventGo = new GameObject("PlayerEventSystem", typeof(MultiplayerEventSystem), typeof(InputSystemUIInputModule));
        eventGo.transform.SetParent(canvasTransform, false);
        multiplayerEventSystem = eventGo.GetComponent<MultiplayerEventSystem>();
        multiplayerEventSystem.playerRoot = canvasTransform.gameObject;
        uiModule = eventGo.GetComponent<InputSystemUIInputModule>();
        ConfigureUiModule();
    }

    void ConfigureUiModule()
    {
        if (playerInput == null)
            TryGetComponent(out playerInput);
        if (uiModule == null || playerInput == null || playerInput.actions == null)
            return;

        InputActionAsset asset = playerInput.actions;
        InputActionMap uiMap = asset.FindActionMap("UI", false);
        if (uiMap == null)
            return;

        // Asignar referencias una a una: solo poner actionsAsset NO cablea Point/Click
        // (HasNoActions() ve el asset y se salta AssignDefaultActions).
        uiModule.actionsAsset = asset;
        uiModule.point = InputActionReference.Create(uiMap.FindAction("Point"));
        uiModule.leftClick = InputActionReference.Create(uiMap.FindAction("Click"));
        uiModule.middleClick = InputActionReference.Create(uiMap.FindAction("MiddleClick"));
        uiModule.rightClick = InputActionReference.Create(uiMap.FindAction("RightClick"));
        uiModule.scrollWheel = InputActionReference.Create(uiMap.FindAction("ScrollWheel"));
        uiModule.move = InputActionReference.Create(uiMap.FindAction("Navigate"));
        uiModule.submit = InputActionReference.Create(uiMap.FindAction("Submit"));
        uiModule.cancel = InputActionReference.Create(uiMap.FindAction("Cancel"));
        playerInput.uiInputModule = uiModule;
    }

    bool IsKeyboardMousePlayer()
    {
        if (playerInput == null)
            return true;
        string scheme = playerInput.currentControlScheme;
        return string.IsNullOrEmpty(scheme) || scheme == "Keyboard&Mouse";
    }

    void EnsureMouseCursorForUi()
    {
        if (!IsKeyboardMousePlayer())
            return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    GameObject BuildMainPanel(Transform parent)
    {
        var panel = new GameObject("MainPanel", typeof(Image));
        panel.transform.SetParent(parent, false);
        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);

        bool anyTest = showTestDamageButton || showTestAlarmButton;
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(420f, anyTest ? 460f : 320f);
        rect.anchoredPosition = Vector2.zero;

        float y = anyTest ? 180f : 110f;
        CreateLabel(panel.transform, "Pausa", new Vector2(0f, y), 36);
        y -= 70f;

        photosButton = CreateButton(panel.transform, "Fotos", new Vector2(0f, y), OnPhotosClicked);
        y -= 70f;
        CreateButton(panel.transform, "Volver al juego", new Vector2(0f, y), OnResumeClicked);
        y -= 70f;
        CreateButton(panel.transform, "Salir", new Vector2(0f, y), OnLeaveClicked);
        y -= 70f;

        if (showTestDamageButton)
        {
            string dmgLabel = "Daño test";
            if (health == null)
                TryGetComponent(out health);
            if (health != null)
                dmgLabel = $"Daño test (-{health.TestDamageAmount:0})";

            testDamageButton = CreateButton(panel.transform, dmgLabel, new Vector2(0f, y), OnTestDamageClicked);
            testDamageButton.gameObject.name = "TestDamageButton";
            y -= 70f;
        }

        if (showTestAlarmButton)
        {
            testAlarmButton = CreateButton(panel.transform, "Alarma test (+0.3)", new Vector2(0f, y), OnTestAlarmClicked);
            testAlarmButton.gameObject.name = "TestAlarmButton";
        }

        return panel;
    }

    void OnTestDamageClicked()
    {
        if (health == null)
            TryGetComponent(out health);
        if (health == null)
        {
            Debug.LogWarning("[Pause] Sin PlayerHealth.");
            return;
        }

        Debug.Log($"[Pause] Daño test → {health.TestDamageAmount} (vida={health.CurrentHealth})");
        health.DealTestDamage();
    }

    void OnTestAlarmClicked()
    {
        AlarmManager alarm = AlarmManager.Instance != null
            ? AlarmManager.Instance
            : Object.FindAnyObjectByType<AlarmManager>();

        if (alarm == null)
        {
            Debug.LogWarning(
                "[Pause] No hay AlarmManager. Colócalo en la escena Gameplay " +
                "(local) o asegúrate de que el prefab online está spawneado.");
            return;
        }

        Debug.Log($"[Pause] Alarma test (actual={alarm.AlarmValue:0.##})");
        alarm.RequestTestRaise();
    }

    GameObject BuildPhotosPanel(Transform parent)
    {
        var panel = new GameObject("PhotosPanel", typeof(Image));
        panel.transform.SetParent(parent, false);
        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.96f);
        // Menos margen en split-screen para no comer ancho útil.
        StretchFull(panel.GetComponent<RectTransform>(), 16f);

        Text title = CreateLabel(panel.transform, "Mis fotos", new Vector2(0f, -16f), 28);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -16f);

        var scrollGo = new GameObject("Scroll", typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(panel.transform, false);
        Image scrollImage = scrollGo.GetComponent<Image>();
        scrollImage.color = new Color(0f, 0f, 0f, 0.25f);

        RectTransform scrollRectTransform = scrollGo.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0f, 0f);
        scrollRectTransform.anchorMax = new Vector2(1f, 1f);
        scrollRectTransform.offsetMin = new Vector2(16f, 72f);
        scrollRectTransform.offsetMax = new Vector2(-16f, -56f);

        var viewportGo = new GameObject("Viewport", typeof(RectMask2D), typeof(Image));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        Image viewportImage = viewportGo.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
        photosScrollViewport = viewportGo.GetComponent<RectTransform>();
        StretchFull(photosScrollViewport);

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportGo.transform, false);
        photosContent = contentGo.GetComponent<RectTransform>();
        photosContent.anchorMin = new Vector2(0f, 1f);
        photosContent.anchorMax = new Vector2(1f, 1f);
        photosContent.pivot = new Vector2(0.5f, 1f);
        photosContent.anchoredPosition = Vector2.zero;
        photosContent.offsetMin = new Vector2(0f, photosContent.offsetMin.y);
        photosContent.offsetMax = new Vector2(0f, photosContent.offsetMax.y);

        photosGrid = contentGo.GetComponent<GridLayoutGroup>();
        photosGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        photosGrid.constraintCount = ThumbColumns;
        photosGrid.spacing = new Vector2(8f, 8f);
        photosGrid.padding = new RectOffset(4, 4, 4, 4);
        photosGrid.childAlignment = TextAnchor.UpperLeft;
        photosGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        photosGrid.startAxis = GridLayoutGroup.Axis.Horizontal;

        ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = photosScrollViewport;
        scroll.content = photosContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        Button backButton = CreateButton(panel.transform, "Volver", new Vector2(0f, 40f), OnPhotosBackClicked);
        backButton.gameObject.name = "BackButton";
        RectTransform backRect = backButton.GetComponent<RectTransform>();
        backRect.anchorMin = new Vector2(0.5f, 0f);
        backRect.anchorMax = new Vector2(0.5f, 0f);
        backRect.pivot = new Vector2(0.5f, 0f);
        backRect.anchoredPosition = new Vector2(0f, 28f);
        backRect.sizeDelta = new Vector2(280f, 52f);

        return panel;
    }

    void UpdatePhotoCellSize()
    {
        if (photosGrid == null)
            return;

        if (rootRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
        if (photosScrollViewport != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(photosScrollViewport);

        // Medir el viewport del scroll (ancho real del jugador), no toda la pantalla.
        float viewportWidth = photosScrollViewport != null && photosScrollViewport.rect.width > 1f
            ? photosScrollViewport.rect.width
            : (rootRect != null ? Mathf.Max(120f, rootRect.rect.width - 64f) : 800f);

        float pad = photosGrid.padding.left + photosGrid.padding.right;
        float spacing = photosGrid.spacing.x * (ThumbColumns - 1);
        float cellW = (viewportWidth - pad - spacing) / ThumbColumns;
        cellW = Mathf.Max(48f, cellW);
        float cellH = cellW * (9f / 16f);
        photosGrid.cellSize = new Vector2(cellW, cellH);
    }

    void RefreshPhotoGrid()
    {
        if (photosContent == null)
            return;

        Canvas.ForceUpdateCanvases();
        UpdatePhotoCellSize();
        EnsureThumbSlotCount(Mathf.Max(album.Count, VisibleThumbSlots));

        for (int i = 0; i < thumbSlots.Count; i++)
        {
            RawImage slot = thumbSlots[i];
            if (i < album.Count)
            {
                slot.texture = album.Photos[i];
                slot.color = Color.white;
                slot.gameObject.SetActive(true);
            }
            else if (i < VisibleThumbSlots)
            {
                slot.texture = null;
                slot.color = new Color(0.2f, 0.2f, 0.22f, 0.8f);
                slot.gameObject.SetActive(true);
            }
            else
            {
                slot.gameObject.SetActive(false);
            }
        }

        if (photosScrollViewport != null)
            photosContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, photosScrollViewport.rect.width);

        LayoutRebuilder.ForceRebuildLayoutImmediate(photosContent);
    }

    void EnsureThumbSlotCount(int count)
    {
        while (thumbSlots.Count < count)
        {
            var slotGo = new GameObject($"Thumb_{thumbSlots.Count}", typeof(RawImage));
            slotGo.transform.SetParent(photosContent, false);
            RawImage raw = slotGo.GetComponent<RawImage>();
            raw.color = new Color(0.2f, 0.2f, 0.22f, 0.8f);
            raw.raycastTarget = false;
            thumbSlots.Add(raw);
        }
    }

    static Text CreateLabel(Transform parent, string text, Vector2 anchoredPos, int fontSize)
    {
        var go = new GameObject("Label", typeof(Text));
        go.transform.SetParent(parent, false);
        Text label = go.GetComponent<Text>();
        label.text = text;
        label.alignment = TextAnchor.MiddleCenter;
        label.font = GetUiFont();
        label.fontSize = fontSize;
        label.color = Color.white;
        label.raycastTarget = false;

        RectTransform rect = label.rectTransform;
        rect.sizeDelta = new Vector2(600f, 50f);
        rect.anchoredPosition = anchoredPos;
        return label;
    }

    static Button CreateButton(Transform parent, string label, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.18f, 0.18f, 0.22f, 1f);

        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.3f, 0.45f, 0.7f, 1f);
        colors.selectedColor = new Color(0.25f, 0.4f, 0.65f, 1f);
        colors.pressedColor = new Color(0.15f, 0.3f, 0.5f, 1f);
        button.colors = colors;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(320f, 56f);
        rect.anchoredPosition = anchoredPos;

        var textGo = new GameObject("Text", typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        Text text = textGo.GetComponent<Text>();
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.font = GetUiFont();
        text.fontSize = 24;
        text.color = Color.white;
        text.raycastTarget = false;

        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    static void StretchFull(RectTransform rect, float margin = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(margin, margin);
        rect.offsetMax = new Vector2(-margin, -margin);
    }

    static Font GetUiFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
            return font;

        return Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 16);
    }
}
