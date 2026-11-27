#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Asegura PlayerMovement en NetworkPlayer.prefab (motor compartido local/online).
/// </summary>
public static class NetworkPlayerMovementSetup
{
    const string PrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";

    [MenuItem("Tools/Paparazzi/Setup NetworkPlayer Shared Movement")]
    public static void SetupMenu() => Setup(showDialog: true);

    public static void Setup(bool showDialog = true)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (!contents.TryGetComponent(out PlayerMovement motor))
                motor = contents.AddComponent<PlayerMovement>();

            Transform pivot = contents.transform.Find("CameraPivot");
            var so = new SerializedObject(motor);
            if (pivot != null)
                so.FindProperty("cameraPivot").objectReferenceValue = pivot;
            so.FindProperty("walkSpeed").floatValue = 3f;
            so.FindProperty("sprintSpeed").floatValue = 6f;
            so.FindProperty("jumpHeight").floatValue = 1.2f;
            so.FindProperty("gravity").floatValue = -50f;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            string msg =
                "NetworkPlayer: PlayerMovement listo (walk 3, sprint 6, gravity -50).\n" +
                "Local/Gym usan este componente; online lo conduce NetworkPlayerController.";
            Debug.Log("[NetworkPlayerMovementSetup] " + msg);
            if (showDialog)
                EditorUtility.DisplayDialog("NetworkPlayer Movement", msg, "OK");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
#endif
