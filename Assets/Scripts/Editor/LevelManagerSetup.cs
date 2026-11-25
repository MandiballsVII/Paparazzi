#if UNITY_EDITOR
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Crea Assets/Prefabs/LevelManager.prefab (RoundSession + Alarm + CelebritySpawner)
/// y opcionalmente lo coloca en la escena Gameplay.
/// </summary>
public static class LevelManagerSetup
{
    const string PrefabFolder = "Assets/Prefabs";
    const string PrefabPath = PrefabFolder + "/LevelManager.prefab";
    const string RoundSessionPath = PrefabFolder + "/RoundSession.prefab";
    const string CelebrityPath = PrefabFolder + "/Celebrity.prefab";
    const string NetworkCelebrityPath = PrefabFolder + "/NetworkCelebrity.prefab";
    const string GameplayPath = "Assets/Scenes/Gameplay.unity";
    const string DefaultPrefabsPath = "Assets/DefaultPrefabObjects.asset";

    [MenuItem("Tools/Paparazzi/Setup Level Manager Prefab")]
    public static void SetupLevelManagerPrefabMenu() => SetupLevelManagerPrefab(showDialog: true);

    [MenuItem("Tools/Paparazzi/Place Level Manager In Gameplay")]
    public static void PlaceInGameplayMenu()
    {
        SetupLevelManagerPrefab(showDialog: false);
        PlaceInGameplay(showDialog: true);
    }

    public static void SetupLevelManagerPrefab(bool showDialog = true)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        // Asegura RoundSession con extract + alarm.
        RoundSessionSetup.SetupRoundSessionPrefab(showDialog: false);
        AlarmManagerSetup.SetupAlarmManager(showDialog: false);

        var root = new GameObject("LevelManager");
        LevelManager level = root.AddComponent<LevelManager>();
        CelebritySpawner celebritySpawner = root.AddComponent<CelebritySpawner>();

        GameObject roundGo = new GameObject("RoundSession");
        roundGo.transform.SetParent(root.transform, false);
        NetworkObject roundNob = roundGo.AddComponent<NetworkObject>();
        NetworkRoundExtract extract = roundGo.AddComponent<NetworkRoundExtract>();
        AlarmManager alarm = roundGo.AddComponent<AlarmManager>();
        roundGo.AddComponent<NetworkAlarmManager>();
        CelebrityNetworkUtility.TryReserializeNetworkObject(roundNob);

        GameObject localCeleb = AssetDatabase.LoadAssetAtPath<GameObject>(CelebrityPath);
        NetworkObject networkCeleb = null;
        GameObject netGo = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkCelebrityPath);
        if (netGo != null)
            networkCeleb = netGo.GetComponent<NetworkObject>();

        level.EditorAssign(extract, alarm, celebritySpawner, localCeleb, networkCeleb);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        // RoundSession hijo también como spawnable por si se usa aparte.
        if (saved != null)
        {
            NetworkRoundExtract childExtract = saved.GetComponentInChildren<NetworkRoundExtract>(true);
            if (childExtract != null)
                RegisterInDefaultPrefabs(childExtract.gameObject);
        }

        // Mantener RoundSession.prefab alineado (extract + alarm).
        SyncStandaloneRoundSessionPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg =
            "LevelManager.prefab creado en Assets/Prefabs.\n\n" +
            "Siguiente:\n" +
            "1) Tools > Paparazzi > Place Level Manager In Gameplay\n" +
            "   (o arrastra el prefab a cada escena de gameplay).\n" +
            "2) Borra RoundSessionSpawner / AlarmManagerSpawner sueltos de la escena.\n" +
            "3) Comprueba Celebrity prefabs en el LevelManager.\n" +
            "4) Si FishNet se queja: Reserialize NetworkObjects (Prefabs).";
        Debug.Log("[LevelManagerSetup] " + msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Level Manager", msg, "OK");
    }

    public static void PlaceInGameplay(bool showDialog = true)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Level Manager", "No existe LevelManager.prefab. Ejecuta Setup primero.", "OK");
            return;
        }

        Scene gameplay = EditorSceneManager.OpenScene(GameplayPath, OpenSceneMode.Single);
        foreach (GameObject root in gameplay.GetRootGameObjects())
        {
            if (root.name == "LevelManager" || root.GetComponent<LevelManager>() != null)
                Object.DestroyImmediate(root);
            if (root.name == "RoundSessionSpawner" || root.GetComponent<RoundSessionSpawner>() != null)
                Object.DestroyImmediate(root);
            if (root.name == "AlarmManagerSpawner" || root.GetComponent<AlarmManagerSpawner>() != null)
                Object.DestroyImmediate(root);
            if (root.name == "CelebritySpawner" && root.GetComponent<LevelManager>() == null)
                Object.DestroyImmediate(root);
        }

        // AlarmManager suelto en escena (legado): quitar si LevelManager ya trae uno.
        AlarmManager[] alarms = Object.FindObjectsByType<AlarmManager>(FindObjectsSortMode.None);
        for (int i = 0; i < alarms.Length; i++)
        {
            if (alarms[i] != null && alarms[i].GetComponentInParent<LevelManager>() == null)
                Object.DestroyImmediate(alarms[i].gameObject);
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "LevelManager";
        Undo.RegisterCreatedObjectUndo(instance, "Place Level Manager");

        EditorSceneManager.MarkSceneDirty(gameplay);
        EditorSceneManager.SaveScene(gameplay);
        Selection.activeGameObject = instance;

        string msg =
            "LevelManager colocado en Gameplay.\n" +
            "Spawners legacy Round/Alarm/Celebrity sueltos eliminados si existían.";
        Debug.Log("[LevelManagerSetup] " + msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Level Manager", msg, "OK");
    }

    static void SyncStandaloneRoundSessionPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(RoundSessionPath);
        if (existing == null)
        {
            RoundSessionSetup.SetupRoundSessionPrefab(showDialog: false);
            AlarmManagerSetup.SetupAlarmManager(showDialog: false);
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(RoundSessionPath);
        if (contents.GetComponent<NetworkObject>() == null)
            contents.AddComponent<NetworkObject>();
        if (contents.GetComponent<NetworkRoundExtract>() == null)
            contents.AddComponent<NetworkRoundExtract>();
        if (contents.GetComponent<AlarmManager>() == null)
            contents.AddComponent<AlarmManager>();
        if (contents.GetComponent<NetworkAlarmManager>() == null)
            contents.AddComponent<NetworkAlarmManager>();
        if (contents.TryGetComponent(out NetworkObject nob))
            CelebrityNetworkUtility.TryReserializeNetworkObject(nob);
        PrefabUtility.SaveAsPrefabAsset(contents, RoundSessionPath);
        PrefabUtility.UnloadPrefabContents(contents);
        RegisterInDefaultPrefabs(AssetDatabase.LoadAssetAtPath<GameObject>(RoundSessionPath));
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
            if (AssetDatabase.GetAssetPath(existing) == AssetDatabase.GetAssetPath(nob))
                return;
        }

        defaults.AddObject(nob, checkForDuplicates: true, initializeAdded: true);
        EditorUtility.SetDirty(defaults);
    }
}
#endif
