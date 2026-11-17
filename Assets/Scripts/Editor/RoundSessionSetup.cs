#if UNITY_EDITOR
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Prefab RoundSession (NetworkRoundExtract) + DefaultPrefabObjects.
/// </summary>
public static class RoundSessionSetup
{
    const string PrefabFolder = "Assets/Prefabs";
    const string RoundSessionPath = PrefabFolder + "/RoundSession.prefab";
    const string DefaultPrefabsPath = "Assets/DefaultPrefabObjects.asset";

    [MenuItem("Tools/Paparazzi/Setup Round Session Prefab")]
    public static void SetupRoundSessionPrefabMenu() => SetupRoundSessionPrefab(showDialog: true);

    public static void SetupRoundSessionPrefab(bool showDialog = true)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(RoundSessionPath);
        if (existing == null)
        {
            var go = new GameObject("RoundSession");
            NetworkObject nob = go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkRoundExtract>();
            CelebrityNetworkUtility.TryReserializeNetworkObject(nob);
            PrefabUtility.SaveAsPrefabAsset(go, RoundSessionPath);
            Object.DestroyImmediate(go);
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(RoundSessionPath);
        if (contents.GetComponent<NetworkObject>() == null)
            contents.AddComponent<NetworkObject>();
        if (contents.GetComponent<NetworkRoundExtract>() == null)
            contents.AddComponent<NetworkRoundExtract>();
        if (contents.TryGetComponent(out NetworkObject savedNob))
            CelebrityNetworkUtility.TryReserializeNetworkObject(savedNob);
        PrefabUtility.SaveAsPrefabAsset(contents, RoundSessionPath);
        PrefabUtility.UnloadPrefabContents(contents);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoundSessionPath);
        RegisterInDefaultPrefabs(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg =
            "RoundSession.prefab listo y registrado.\n" +
            "Si FishNet se queja: Fish-Networking > Utility > Reserialize NetworkObjects (Prefabs).";
        Debug.Log("[RoundSessionSetup] " + msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Round Session", msg, "OK");
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
