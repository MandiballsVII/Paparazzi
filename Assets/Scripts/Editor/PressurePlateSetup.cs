#if UNITY_EDITOR
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Crea Assets/Prefabs/PressurePlateSensor.prefab y lo registra en DefaultPrefabObjects.
/// </summary>
public static class PressurePlateSetup
{
    const string PrefabFolder = "Assets/Prefabs";
    const string PrefabPath = PrefabFolder + "/PressurePlateSensor.prefab";
    const string DefaultPrefabsPath = "Assets/DefaultPrefabObjects.asset";

    [MenuItem("Tools/Paparazzi/Setup Pressure Plate Sensor Prefab")]
    public static void SetupPressurePlatePrefabMenu() => SetupPressurePlatePrefab(showDialog: true);

    public static void SetupPressurePlatePrefab(bool showDialog = true)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "PressurePlateSensor";
        Object.DestroyImmediate(root.GetComponent<BoxCollider>());

        root.transform.localScale = new Vector3(1f, 0.04f, 1f);

        MeshRenderer renderer = root.GetComponent<MeshRenderer>();
        Material mat = CreateDefaultPlateMaterial();
        if (mat != null)
            renderer.sharedMaterial = mat;

        // Trigger bajo: el jugador puede saltarlo; props dinámicos lo activan.
        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(1f, 4f, 1f); // en local scale del cube aplastado → altura mundo ~0.16
        trigger.center = new Vector3(0f, 2.5f, 0f);

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        root.AddComponent<PressurePlateSensor>();
        root.AddComponent<NetworkObject>();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        RegisterInDefaultPrefabs(saved);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg =
            "PressurePlateSensor.prefab creado en Assets/Prefabs.\n" +
            "Asigna el material del suelo del nivel en el MeshRenderer.\n" +
            "Colócalo en la escena (local u online). Cooldown por defecto: 5s.\n" +
            "Si FishNet se queja: Fish-Networking > Utility > Reserialize NetworkObjects (Prefabs).";
        Debug.Log("[PressurePlateSetup] " + msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Pressure Plate", msg, "OK");
    }

    static Material CreateDefaultPlateMaterial()
    {
        Shader shader = GraphicsSettings.currentRenderPipeline != null
            ? Shader.Find("Universal Render Pipeline/Lit")
            : Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            return null;

        var mat = new Material(shader);
        mat.name = "PressurePlate_Default";
        mat.color = new Color(0.55f, 0.45f, 0.35f, 1f);
        AssetDatabase.CreateAsset(mat, PrefabFolder + "/PressurePlate_Default.mat");
        return mat;
    }

    static void RegisterInDefaultPrefabs(GameObject prefabRoot)
    {
        DefaultPrefabObjects defaults = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabsPath);
        if (defaults == null || prefabRoot == null)
            return;
        if (!prefabRoot.TryGetComponent(out NetworkObject nob))
            return;

        int count = defaults.GetObjectCount();
        for (int i = 0; i < count; i++)
        {
            NetworkObject existing = defaults.GetObject(true, i);
            if (existing == null)
                continue;
            if (existing == nob)
                return;
            if (existing.gameObject != null && existing.gameObject.name == prefabRoot.name)
                return;
        }

        defaults.AddObject(nob);
        EditorUtility.SetDirty(defaults);
    }
}
#endif
