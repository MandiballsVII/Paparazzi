using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Evita que varios PlayerInput compartan el mismo InputActionAsset
/// (mapa Pause/UI global → pausa en todos los jugadores).
/// </summary>
public static class PlayerInputUtility
{
    public static void EnsurePrivateActionAsset(PlayerInput playerInput)
    {
        if (playerInput == null)
            return;

        if (playerInput.TryGetComponent(out PrivateInputActionsMarker _))
            return;

        ClonePrivateActionAsset(playerInput);
    }

    /// <summary>
    /// Clones online → Lobby: el marker se copia en el Instantiate y hay que regenerar asset propio.
    /// </summary>
    public static void ForcePrivateActionAsset(PlayerInput playerInput)
    {
        if (playerInput == null)
            return;

        if (playerInput.TryGetComponent(out PrivateInputActionsMarker marker))
            Object.DestroyImmediate(marker);

        ClonePrivateActionAsset(playerInput);
    }

    static void ClonePrivateActionAsset(PlayerInput playerInput)
    {
        InputActionAsset source = playerInput.actions != null
            ? playerInput.actions
            : InputSystem.actions;

        if (source == null)
        {
            Debug.LogError("[PlayerInputUtility] No hay Input Action Asset (Project Settings > Input System).");
            return;
        }

        InputActionAsset copy = Object.Instantiate(source);
        copy.name = source.name + "_P" + playerInput.playerIndex;
        playerInput.actions = copy;
        if (!playerInput.TryGetComponent(out PrivateInputActionsMarker _))
            playerInput.gameObject.AddComponent<PrivateInputActionsMarker>();
    }

    public static bool IsDevicePairedToPlayer(PlayerInput playerInput, InputDevice device)
    {
        if (playerInput == null || device == null)
            return false;

        var devices = playerInput.devices;
        for (int i = 0; i < devices.Count; i++)
        {
            if (devices[i] == device)
                return true;
        }

        return false;
    }

    /// <summary>Marca de que este PlayerInput ya tiene asset clonado en runtime.</summary>
    sealed class PrivateInputActionsMarker : MonoBehaviour
    {
    }
}
