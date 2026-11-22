#if UNITY_EDITOR
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Añade AlarmManager al RoundSession (o crea AlarmManager.prefab) y lo registra.
/// </summary>
public static class AlarmManagerSetup
{
    const string PrefabFolder = "Assets/Prefabs";
    const string RoundSessionPath = PrefabFolder + "/RoundSession.prefab";
    const string AlarmPrefabPath = PrefabFolder + "/AlarmManager.prefab";
    const string DefaultPrefabsPath = "Assets/DefaultPrefabObjects.asset";

    [MenuItem("Tools/Paparazzi/Setup Alarm Manager Prefab")]
    public static void SetupAlarmManagerMenu() => SetupAlarmManager(showDialog: true);

    public static void SetupAlarmManager(bool showDialog = true)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        // Preferible: ir en RoundSession (una sola spawn de estado de ronda).
        GameObject roundSession = AssetDatabase.LoadAssetAtPath<GameObject>(RoundSessionPath);
        if (roundSession != null)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(RoundSessionPath);
            EnsureComponents(contents);
            if (contents.TryGetComponent(out NetworkObject nob))
                CelebrityNetworkUtility.TryReserializeNetworkObject(nob);
            PrefabUtility.SaveAsPrefabAsset(contents, RoundSessionPath);
            PrefabUtility.UnloadPrefabContents(contents);
            RegisterInDefaultPrefabs(AssetDatabase.LoadAssetAtPath<GameObject>(RoundSessionPath));
        }

        // Prefab standalone por si se quiere spawnear aparte.
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(AlarmPrefabPath);
        if (existing == null)
        {
            var go = new GameObject("AlarmManager");
            NetworkObject nob = go.AddComponent<NetworkObject>();
            go.AddComponent<AlarmManager>();
            go.AddComponent<NetworkAlarmManager>();
            CelebrityNetworkUtility.TryReserializeNetworkObject(nob);
            PrefabUtility.SaveAsPrefabAsset(go, AlarmPrefabPath);
            Object.DestroyImmediate(go);
        }
        else
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(AlarmPrefabPath);
            EnsureComponents(contents);
            if (contents.TryGetComponent(out NetworkObject nob))
                CelebrityNetworkUtility.TryReserializeNetworkObject(nob);
            PrefabUtility.SaveAsPrefabAsset(contents, AlarmPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        RegisterInDefaultPrefabs(AssetDatabase.LoadAssetAtPath<GameObject>(AlarmPrefabPath));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg =
            "AlarmManager.prefab listo y registrado.\n\n" +
            "RECOMENDADO (editable):\n" +
            "• Local: coloca un GO 'AlarmManager' + componente AlarmManager en la escena Gameplay.\n" +
            "• Online: coloca el prefab AlarmManager (con NetworkObject) en Gameplay, " +
            "o deja que AlarmManagerSpawner lo spawnee.\n\n" +
            "HUD: Tools > Paparazzi > Setup NetworkPlayer Alarm HUD.";
        Debug.Log("[AlarmManagerSetup] " + msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Alarm Manager", msg, "OK");
    }

    [MenuItem("Tools/Paparazzi/Setup NetworkPlayer Alarm HUD")]
    public static void SetupNetworkPlayerAlarmHud()
    {
        const string path = "Assets/Prefabs/NetworkPlayer.prefab";
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (!contents.TryGetComponent(out PlayerAlarmHud hud))
                hud = contents.AddComponent<PlayerAlarmHud>();

            Transform yellow = FindDeep(contents.transform, "YellowAlarmStars");
            Transform testBtn = FindDeep(contents.transform, "TestAlarm");

            SerializedObject so = new SerializedObject(hud);
            if (yellow != null)
                so.FindProperty("yellowStarsRoot").objectReferenceValue = yellow;
            if (testBtn != null && testBtn.TryGetComponent(out UnityEngine.UI.Button button))
                so.FindProperty("testAlarmButton").objectReferenceValue = button;
            so.FindProperty("showTestAlarmButton").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            Debug.Log("[Alarm] PlayerAlarmHud cableado en NetworkPlayer.");
            EditorUtility.DisplayDialog(
                "Alarm HUD",
                "PlayerAlarmHud añadido/cableado en NetworkPlayer.\nYellowAlarmStars + TestAlarm asignados si existían.",
                "OK");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    static void EnsureComponents(GameObject go)
    {
        if (go.GetComponent<NetworkObject>() == null)
            go.AddComponent<NetworkObject>();
        if (go.GetComponent<AlarmManager>() == null)
            go.AddComponent<AlarmManager>();
        if (go.GetComponent<NetworkAlarmManager>() == null)
            go.AddComponent<NetworkAlarmManager>();
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

    static Transform FindDeep(Transform root, string objectName)
    {
        if (root.name == objectName)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }
}
#endif
