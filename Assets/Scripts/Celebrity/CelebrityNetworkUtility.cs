using System.Reflection;
using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Helpers de Editor/runtime para montar NetworkCelebrity sin AddComponent a ciegas en Play.
/// </summary>
public static class CelebrityNetworkUtility
{
    static readonly MethodInfo ReserializeMethod = typeof(NetworkObject).GetMethod(
        "ReserializeEditorSetValues",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    public static void EnsureNetworkComponentsOnPrefab(GameObject root)
    {
        if (root == null)
            return;

        if (!root.TryGetComponent(out NetworkObject _))
            root.AddComponent<NetworkObject>();

        if (!root.TryGetComponent(out NetworkTransform nt))
            nt = root.AddComponent<NetworkTransform>();

        ConfigureServerAuthNetworkTransform(nt);

        if (!root.TryGetComponent(out NetworkCelebrity _))
            root.AddComponent<NetworkCelebrity>();

        if (!root.TryGetComponent(out CelebrityBrain _))
            root.AddComponent<CelebrityBrain>();

        if (root.TryGetComponent(out NetworkObject nob))
            TryReserializeNetworkObject(nob);
    }

    public static void ConfigureServerAuthNetworkTransform(NetworkTransform nt)
    {
        if (nt == null)
            return;

        var type = typeof(NetworkTransform);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

        type.GetField("_clientAuthoritative", flags)?.SetValue(nt, false);
        type.GetField("_sendToOwner", flags)?.SetValue(nt, true);
        type.GetField("_componentConfiguration", flags)?.SetValue(nt, 0); // Disabled
        type.GetField("_interpolation", flags)?.SetValue(nt, (ushort)1);
        type.GetField("_extrapolation", flags)?.SetValue(nt, (ushort)0);
        type.GetField("_enableTeleport", flags)?.SetValue(nt, true);
        type.GetField("_teleportThreshold", flags)?.SetValue(nt, 0.5f);
    }

    public static void TryReserializeNetworkObject(NetworkObject nob)
    {
        if (nob == null || ReserializeMethod == null)
            return;
        ReserializeMethod.Invoke(nob, new object[] { true, false });
    }
}
