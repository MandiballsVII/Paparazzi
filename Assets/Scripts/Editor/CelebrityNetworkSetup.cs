#if UNITY_EDITOR
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Crea NetworkCelebrity.prefab (copia del Celebrity local + red) y lo registra.
/// </summary>
public static class CelebrityNetworkSetup
{
    const string PrefabFolder = "Assets/Prefabs";
    const string LocalCelebrityPath = PrefabFolder + "/Celebrity.prefab";
    const string NetworkCelebrityPath = PrefabFolder + "/NetworkCelebrity.prefab";
    const string DefaultPrefabsPath = "Assets/DefaultPrefabObjects.asset";

    [MenuItem("Tools/Paparazzi/Setup Network Celebrity Prefab")]
    public static void SetupNetworkCelebrityPrefabMenu() => SetupNetworkCelebrityPrefab(showDialog: true);

    public static void SetupNetworkCelebrityPrefab(bool showDialog = true)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        GameObject local = AssetDatabase.LoadAssetAtPath<GameObject>(LocalCelebrityPath);
        if (local == null)
        {
            EditorUtility.DisplayDialog(
                "Network Celebrity",
                "No existe Assets/Prefabs/Celebrity.prefab. Crea el prefab local primero.",
                "OK");
            return;
        }

        GameObject temp = PrefabUtility.InstantiatePrefab(local) as GameObject;
        if (temp == null)
            temp = Object.Instantiate(local);

        temp.name = "NetworkCelebrity";
        CelebrityNetworkUtility.EnsureNetworkComponentsOnPrefab(temp);

        PrefabUtility.SaveAsPrefabAsset(temp, NetworkCelebrityPath);
        Object.DestroyImmediate(temp);

        GameObject contents = PrefabUtility.LoadPrefabContents(NetworkCelebrityPath);
        CelebrityNetworkUtility.EnsureNetworkComponentsOnPrefab(contents);
        PrefabUtility.SaveAsPrefabAsset(contents, NetworkCelebrityPath);
        PrefabUtility.UnloadPrefabContents(contents);

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkCelebrityPath);
        RegisterInDefaultPrefabs(saved);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg =
            "NetworkCelebrity.prefab listo y registrado en DefaultPrefabObjects.\n" +
            "En GameplayBootstrap asigna Network Celebrity Prefab si no se auto-carga.\n" +
            "Si FishNet se queja: Fish-Networking > Utility > Reserialize NetworkObjects (Prefabs).";
        Debug.Log("[CelebrityNetworkSetup] " + msg);
        if (showDialog)
            EditorUtility.DisplayDialog("Network Celebrity", msg, "OK");
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
