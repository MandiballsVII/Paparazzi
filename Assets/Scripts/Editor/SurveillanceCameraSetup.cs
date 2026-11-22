#if UNITY_EDITOR
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Prefab SurveillanceCamera: Mount (cilindro) + HeadPivot (yaw) + Head (pitch) + Body prisma + Eye.
/// </summary>
public static class SurveillanceCameraSetup
{
    const string PrefabFolder = "Assets/Prefabs";
    const string PrefabPath = PrefabFolder + "/SurveillanceCamera.prefab";
    const string MatPath = PrefabFolder + "/SurveillanceCamera_Default.mat";
    const string DefaultPrefabsPath = "Assets/DefaultPrefabObjects.asset";

    const float LookDownPitch = 35f;
    const float HeadLength = 0.32f;
    const float HeadWidth = 0.11f;
    const float HeadHeight = 0.09f;
    const float MountRadius = 0.035f;
    const float MountLength = 0.1f;

    [MenuItem("Tools/Paparazzi/Setup Surveillance Camera Prefab")]
    public static void SetupSurveillanceCameraPrefabMenu() => SetupSurveillanceCameraPrefab(showDialog: true);

    public static void SetupSurveillanceCameraPrefab(bool showDialog = true)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        Material mat = EnsureMaterial();

        var root = new GameObject("SurveillanceCamera");

        // Mount: cilindro de unión. Unity cylinder eje Y → rotar a Z (sale de la pared/techo).
        GameObject mount = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mount.name = "Mount";
        mount.transform.SetParent(root.transform, false);
        mount.transform.localPosition = new Vector3(0f, 0f, MountLength * 0.5f);
        mount.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        mount.transform.localScale = new Vector3(MountRadius * 2f, MountLength * 0.5f, MountRadius * 2f);
        ApplyMat(mount, mat);
        StripCollider(mount);

        // HeadPivot en la junta montura→cabeza (solo yaw / pan móvil).
        var headPivotGo = new GameObject("HeadPivot");
        headPivotGo.transform.SetParent(root.transform, false);
        headPivotGo.transform.localPosition = new Vector3(0f, 0f, MountLength);
        headPivotGo.transform.localRotation = Quaternion.identity;

        // Head: vacío en la junta; pitch fijo hacia abajo (el prisma crece hacia el frontal).
        var headGo = new GameObject("Head");
        headGo.transform.SetParent(headPivotGo.transform, false);
        headGo.transform.localPosition = Vector3.zero;
        headGo.transform.localRotation = Quaternion.Euler(LookDownPitch, 0f, 0f);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(headGo.transform, false);
        body.transform.localPosition = new Vector3(0f, 0f, HeadLength * 0.5f);
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = new Vector3(HeadWidth, HeadHeight, HeadLength);
        ApplyMat(body, mat);
        StripCollider(body);

        var eyeGo = new GameObject("Eye");
        eyeGo.transform.SetParent(headGo.transform, false);
        eyeGo.transform.localPosition = new Vector3(0f, 0f, HeadLength);
        eyeGo.transform.localRotation = Quaternion.identity;

        PerceptionAgent perception = headGo.AddComponent<PerceptionAgent>();
        VisionSensor vision = headGo.GetComponent<VisionSensor>();
        if (vision == null)
            vision = headGo.AddComponent<VisionSensor>();

        ConfigurePerception(perception);
        ConfigureVision(vision, eyeGo.transform);

        SurveillanceCamera cam = root.AddComponent<SurveillanceCamera>();
        root.AddComponent<NetworkObject>();
        WireCamera(cam, headPivotGo.transform, headGo.transform, perception);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        RegisterInDefaultPrefabs(saved);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg =
            "SurveillanceCamera.prefab creado en Assets/Prefabs.\n" +
            "Body = prisma alargado desde el cilindro; Head pitch abajo " + LookDownPitch + "°.\n" +
            "Inspector: Is Mobile, Alarm Raise (0.3), Cooldown (3s), Yaw Sweep (75°).\n" +
            "Menú: Tools/Paparazzi/Setup Surveillance Camera Prefab.\n" +
            "Si FishNet se queja: Fish-Networking > Utility > Reserialize NetworkObjects (Prefabs).";
        Debug.Log("[SurveillanceCameraSetup] " + msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Surveillance Camera", msg, "OK");
    }

    static void ConfigurePerception(PerceptionAgent perception)
    {
        var so = new SerializedObject(perception);
        so.FindProperty("hasVision").boolValue = true;
        so.FindProperty("hasHearing").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureVision(VisionSensor vision, Transform eye)
    {
        var so = new SerializedObject(vision);
        so.FindProperty("eyeHeight").floatValue = 0f;
        so.FindProperty("eyePoint").objectReferenceValue = eye;
        so.FindProperty("viewDistance").floatValue = 16f;
        so.FindProperty("fieldOfView").floatValue = 70f;
        so.FindProperty("scanInterval").floatValue = 0.15f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WireCamera(
        SurveillanceCamera cam,
        Transform headPivot,
        Transform head,
        PerceptionAgent perception)
    {
        var so = new SerializedObject(cam);
        so.FindProperty("isMobile").boolValue = false;
        so.FindProperty("alarmRaiseAmount").floatValue = 0.3f;
        so.FindProperty("detectionCooldownSeconds").floatValue = 3f;
        so.FindProperty("yawSweepDegrees").floatValue = 75f;
        so.FindProperty("panSpeedDegreesPerSecond").floatValue = 20f;
        so.FindProperty("lookDownPitchDegrees").floatValue = LookDownPitch;
        so.FindProperty("headPivot").objectReferenceValue = headPivot;
        so.FindProperty("head").objectReferenceValue = head;
        so.FindProperty("perception").objectReferenceValue = perception;
        so.FindProperty("cameraEnabled").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static Material EnsureMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (existing != null)
            return existing;

        Shader shader = GraphicsSettings.currentRenderPipeline != null
            ? Shader.Find("Universal Render Pipeline/Lit")
            : Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            return null;

        var mat = new Material(shader);
        mat.name = "SurveillanceCamera_Default";
        mat.color = new Color(0.18f, 0.18f, 0.2f, 1f);
        AssetDatabase.CreateAsset(mat, MatPath);
        return mat;
    }

    static void ApplyMat(GameObject go, Material mat)
    {
        if (mat == null || !go.TryGetComponent(out MeshRenderer renderer))
            return;
        renderer.sharedMaterial = mat;
    }

    static void StripCollider(GameObject go)
    {
        Collider col = go.GetComponent<Collider>();
        if (col != null)
            Object.DestroyImmediate(col);
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
