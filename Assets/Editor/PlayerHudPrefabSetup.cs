#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Genera/actualiza la HUD de vida en el prefab NetworkPlayer para editarla en el Editor.
/// </summary>
public static class PlayerHudPrefabSetup
{
    const string NetworkPlayerPrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";

    [MenuItem("Tools/Paparazzi/Setup NetworkPlayer Health HUD")]
    public static void SetupNetworkPlayerHud()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(NetworkPlayerPrefabPath);
        try
        {
            Transform pivot = prefabRoot.transform.Find("CameraPivot");
            Camera cam = pivot != null
                ? pivot.GetComponentInChildren<Camera>(true)
                : prefabRoot.GetComponentInChildren<Camera>(true);

            if (cam == null)
            {
                EditorUtility.DisplayDialog(
                    "Player HUD",
                    "No se encontró PlayerCamera en NetworkPlayer.",
                    "OK");
                return;
            }

            if (!prefabRoot.TryGetComponent(out PlayerHealthHud hud))
                hud = prefabRoot.AddComponent<PlayerHealthHud>();

            Transform existing = cam.transform.Find("PlayerHud");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            GameObject root = CreateHudHierarchy(cam);
            SerializedObject so = new SerializedObject(hud);
            so.FindProperty("playerCamera").objectReferenceValue = cam;
            so.FindProperty("hudRoot").objectReferenceValue = root;
            so.FindProperty("hudCanvas").objectReferenceValue = root.GetComponent<Canvas>();
            so.FindProperty("healthText").objectReferenceValue = root.transform.Find("HealthText").GetComponent<TMP_Text>();
            so.FindProperty("deadText").objectReferenceValue = root.transform.Find("DeadText").GetComponent<TMP_Text>();
            so.FindProperty("testDamageButton").objectReferenceValue = root.transform.Find("TestDamageButton").GetComponent<Button>();
            so.FindProperty("testDamageButtonLabel").objectReferenceValue =
                root.transform.Find("TestDamageButton/Label").GetComponent<TMP_Text>();
            so.FindProperty("showTestDamageButton").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, NetworkPlayerPrefabPath);
            Debug.Log("[Paparazzi] HUD de vida instalada en NetworkPlayer.prefab (CameraPivot/PlayerCamera/PlayerHud).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    static GameObject CreateHudHierarchy(Camera cam)
    {
        var root = new GameObject("PlayerHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(cam.transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.2f;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        rootRt.localScale = Vector3.one;

        CreateTmp(root.transform, "HealthText", new Vector2(24f, -24f), new Vector2(420f, 48f), 36, "100/100");
        TMP_Text dead = CreateTmp(
            root.transform,
            "DeadText",
            new Vector2(24f, -72f),
            new Vector2(720f, 40f),
            28,
            "MUERTO — llévalo al vehículo para revivir");
        dead.gameObject.SetActive(false);

        CreateButton(root.transform, "TestDamageButton", new Vector2(24f, -120f), new Vector2(220f, 44f), "Daño test");
        return root;
    }

    static TMP_Text CreateTmp(
        Transform parent,
        string objectName,
        Vector2 anchoredPos,
        Vector2 size,
        float fontSize,
        string text)
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
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        return tmp;
    }

    static void CreateButton(
        Transform parent,
        string objectName,
        Vector2 anchoredPos,
        Vector2 size,
        string labelText)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        go.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f, 0.85f);

        TMP_Text label = CreateTmp(go.transform, "Label", Vector2.zero, size, 24, labelText);
        RectTransform labelRt = label.rectTransform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.pivot = new Vector2(0.5f, 0.5f);
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
    }
}
#endif
