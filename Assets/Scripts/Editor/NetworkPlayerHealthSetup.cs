#if UNITY_EDITOR
using FishNet.Object;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Asegura NetworkPlayerHealth en el prefab (nunca AddComponent en runtime).
/// </summary>
public static class NetworkPlayerHealthSetup
{
    const string PrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";

    [MenuItem("Tools/Paparazzi/Setup NetworkPlayer Health Network")]
    public static void Setup()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (!contents.TryGetComponent(out PlayerHealth _))
                contents.AddComponent<PlayerHealth>();

            if (!contents.TryGetComponent(out NetworkPlayerHealth _))
                contents.AddComponent<NetworkPlayerHealth>();

            if (contents.TryGetComponent(out NetworkObject nob))
                CelebrityNetworkUtility.TryReserializeNetworkObject(nob);

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            const string msg =
                "NetworkPlayerHealth añadido/reserializado en NetworkPlayer.prefab.\n" +
                "Si FishNet se queja: Fish-Networking > Utility > Reserialize NetworkObjects.";
            Debug.Log("[NetworkPlayerHealthSetup] " + msg);
            EditorUtility.DisplayDialog("NetworkPlayer Health", msg, "OK");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
#endif
