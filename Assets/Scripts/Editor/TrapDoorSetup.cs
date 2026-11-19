#if UNITY_EDITOR
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Crea InfraredSensor.prefab (stub) y TrapDoor.prefab (puertas + placa + IR).
/// </summary>
public static class TrapDoorSetup
{
    const string PrefabFolder = "Assets/Prefabs";
    const string TrapPrefabPath = PrefabFolder + "/TrapDoor.prefab";
    const string InfraredPrefabPath = PrefabFolder + "/InfraredSensor.prefab";
    const string DefaultPrefabsPath = "Assets/DefaultPrefabObjects.asset";

    [MenuItem("Tools/Paparazzi/Setup Trap Door Prefab")]
    public static void SetupTrapDoorPrefabMenu() => SetupTrapDoorPrefab(showDialog: true);

    /// <summary>Entry point para Unity -batchmode -executeMethod TrapDoorSetup.SetupTrapDoorPrefabBatch.</summary>
    public static void SetupTrapDoorPrefabBatch() => SetupTrapDoorPrefab(showDialog: false);

    public static void SetupTrapDoorPrefab(bool showDialog = true)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        GameObject infraredPrefab = CreateInfraredSensorPrefab();
        GameObject trapRoot = BuildTrapDoorHierarchy(infraredPrefab);

        PrefabUtility.SaveAsPrefabAsset(trapRoot, TrapPrefabPath);
        Object.DestroyImmediate(trapRoot);

        GameObject savedTrap = AssetDatabase.LoadAssetAtPath<GameObject>(TrapPrefabPath);
        RegisterInDefaultPrefabs(savedTrap);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg =
            "TrapDoor.prefab + InfraredSensor.prefab creados en Assets/Prefabs.\n" +
            "Por defecto modo PressurePlate (IR hijo desactivado).\n" +
            "Al inicio de ronda: TrapController.ConfigureSensorMode(...).\n" +
            "Si FishNet se queja: Reserialize NetworkObjects (Prefabs).";
        Debug.Log("[TrapDoorSetup] " + msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Trap Door", msg, "OK");
    }

    static GameObject CreateInfraredSensorPrefab()
    {
        var root = new GameObject("InfraredSensor");
        root.AddComponent<InfraredSensor>();

        CreateTube(root.transform, "TubeA", new Vector3(-0.08f, 0.04f, 0f));
        CreateTube(root.transform, "TubeB", new Vector3(0.08f, 0.04f, 0f));

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, InfraredPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void CreateTube(Transform parent, string name, Vector3 localPos)
    {
        GameObject tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tube.name = name;
        tube.transform.SetParent(parent, false);
        tube.transform.localPosition = localPos;
        tube.transform.localScale = new Vector3(0.06f, 0.04f, 0.06f);

        Object.DestroyImmediate(tube.GetComponent<CapsuleCollider>());

        MeshRenderer renderer = tube.GetComponent<MeshRenderer>();
        Material mat = GetOrCreateSharedMaterial(
            PrefabFolder + "/InfraredTube_Default.mat",
            "InfraredTube_Default",
            new Color(0.15f, 0.15f, 0.18f, 1f));
        if (mat != null)
            renderer.sharedMaterial = mat;
    }

    static GameObject BuildTrapDoorHierarchy(GameObject infraredPrefab)
    {
        var root = new GameObject("TrapDoor");

        // Tamaño total ~2x2. Bisagras en bordes exteriores (eje Z).
        Transform pivotLeft = CreatePivot(root.transform, "PivotL", new Vector3(-1f, 0f, 0f));
        Transform pivotRight = CreatePivot(root.transform, "PivotR", new Vector3(1f, 0f, 0f));

        CreateDoorPanel(pivotLeft, "PanelL", new Vector3(0.5f, 0f, 0f));
        Transform panelRight = CreateDoorPanel(pivotRight, "PanelR", new Vector3(-0.5f, 0f, 0f));

        // Sensores en el centro (junta), hijos de una puerta para que caigan al abrirse.
        var sensors = new GameObject("Sensors");
        sensors.transform.SetParent(panelRight, false);
        sensors.transform.localPosition = new Vector3(-0.5f, 0.03f, 0f);

        GameObject plate = CreateEmbeddedPressurePlate(sensors.transform);
        GameObject irInstance = (GameObject)PrefabUtility.InstantiatePrefab(infraredPrefab);
        irInstance.name = "InfraredSensor";
        irInstance.transform.SetParent(sensors.transform, false);
        irInstance.transform.localPosition = Vector3.zero;
        irInstance.SetActive(false);

        TrapController trap = root.AddComponent<TrapController>();
        root.AddComponent<NetworkObject>();

        MeshRenderer panelLeftRenderer = pivotLeft.Find("PanelL")?.GetComponent<MeshRenderer>();
        MeshRenderer panelRightRenderer = pivotRight.Find("PanelR")?.GetComponent<MeshRenderer>();

        SerializedObject so = new SerializedObject(trap);
        so.FindProperty("pivotLeft").objectReferenceValue = pivotLeft;
        so.FindProperty("pivotRight").objectReferenceValue = pivotRight;
        so.FindProperty("panelLeftRenderer").objectReferenceValue = panelLeftRenderer;
        so.FindProperty("panelRightRenderer").objectReferenceValue = panelRightRenderer;
        so.FindProperty("pressureSensorRoot").objectReferenceValue = plate;
        so.FindProperty("infraredSensorRoot").objectReferenceValue = irInstance;
        so.FindProperty("activeMode").enumValueIndex = (int)TrapSensorMode.PressurePlate;
        so.ApplyModifiedPropertiesWithoutUndo();

        return root;
    }

    static Transform CreatePivot(Transform parent, string name, Vector3 localPos)
    {
        var pivot = new GameObject(name);
        pivot.transform.SetParent(parent, false);
        pivot.transform.localPosition = localPos;
        return pivot.transform;
    }

    static Transform CreateDoorPanel(Transform pivot, string name, Vector3 localPos)
    {
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = name;
        panel.transform.SetParent(pivot, false);
        panel.transform.localPosition = localPos;
        panel.transform.localScale = new Vector3(1f, 0.05f, 2f);

        Material mat = GetOrCreateSharedMaterial(
            PrefabFolder + "/TrapDoorPanel_Default.mat",
            "TrapDoorPanel_Default",
            new Color(0.35f, 0.32f, 0.28f, 1f));
        if (mat != null)
            panel.GetComponent<MeshRenderer>().sharedMaterial = mat;

        // Collider sólido para que el jugador se caiga al abrir.
        BoxCollider col = panel.GetComponent<BoxCollider>();
        if (col != null)
            col.isTrigger = false;

        return panel.transform;
    }

    static GameObject CreateEmbeddedPressurePlate(Transform parent)
    {
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "PressurePlateSensor";
        plate.transform.SetParent(parent, false);
        plate.transform.localPosition = Vector3.zero;
        plate.transform.localScale = new Vector3(0.55f, 0.03f, 0.55f);

        Object.DestroyImmediate(plate.GetComponent<BoxCollider>());

        Material mat = GetOrCreateSharedMaterial(
            PrefabFolder + "/PressurePlate_Default.mat",
            "PressurePlate_Default",
            new Color(0.55f, 0.45f, 0.35f, 1f));
        if (mat != null)
            plate.GetComponent<MeshRenderer>().sharedMaterial = mat;

        BoxCollider trigger = plate.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(1f, 4f, 1f);
        trigger.center = new Vector3(0f, 2.5f, 0f);

        Rigidbody body = plate.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // Sin NetworkObject propio: lo cubre el de TrapDoor.
        plate.AddComponent<PressurePlateSensor>();
        return plate;
    }

    static Material GetOrCreateSharedMaterial(string assetPath, string matName, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (existing != null)
            return existing;

        Shader shader = GraphicsSettings.currentRenderPipeline != null
            ? Shader.Find("Universal Render Pipeline/Lit")
            : Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            return null;

        var mat = new Material(shader)
        {
            name = matName,
            color = color
        };
        AssetDatabase.CreateAsset(mat, assetPath);
        return mat;
    }

    static void RegisterInDefaultPrefabs(GameObject prefabRoot)
    {
        DefaultPrefabObjects defaults = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabsPath);
        if (defaults == null || prefabRoot == null)
            return;
        if (!prefabRoot.TryGetComponent(out NetworkObject nob))
            return;

        defaults.AddObject(nob, checkForDuplicates: true, initializeAdded: true);
        EditorUtility.SetDirty(defaults);
    }
}
#endif
