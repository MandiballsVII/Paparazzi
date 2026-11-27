#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Asegura PlayerGrab + NetworkPlayerGrab en NetworkPlayer.prefab.
/// </summary>
public static class NetworkPlayerGrabSetup
{
    const string PrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";

    [MenuItem("Tools/Paparazzi/Setup NetworkPlayer Shared Grab")]
    public static void SetupMenu() => Setup(showDialog: true);

    public static void Setup(bool showDialog = true)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (!contents.TryGetComponent(out PlayerGrab grab))
                grab = contents.AddComponent<PlayerGrab>();

            if (!contents.TryGetComponent(out NetworkPlayerGrab _))
                contents.AddComponent<NetworkPlayerGrab>();

            var so = new SerializedObject(grab);
            so.FindProperty("grabDistance").floatValue = 3f;
            so.FindProperty("holdDistance").floatValue = 2.2f;
            so.FindProperty("maxGrabMass").floatValue = 25f;
            so.FindProperty("holdNetworkSyncInterval").floatValue = 0.05f;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            string msg =
                "NetworkPlayer: PlayerGrab listo (holdDistance 2.2).\n" +
                "Local/Gym usan PlayerGrab; online lo conduce NetworkPlayerGrab.";
            Debug.Log("[NetworkPlayerGrabSetup] " + msg);
            if (showDialog)
                EditorUtility.DisplayDialog("NetworkPlayer Grab", msg, "OK");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
#endif
