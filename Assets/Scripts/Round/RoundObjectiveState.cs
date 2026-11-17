using UnityEngine;

/// <summary>
/// Estado de objetivo de la ronda. La foto válida se registra al disparar.
/// </summary>
public static class RoundObjectiveState
{
    public static bool HasRegisteredControversialShot { get; private set; }

    public static bool PendingNetworkVictoryPhotoRegister { get; private set; }

    public static void NotifyControversialActionShot(PhotoShot shot)
    {
        if (shot != null && !shot.IsControversialActionShot)
            return;

        HasRegisteredControversialShot = true;
        if (shot != null)
            Debug.Log("[RoundObjective] Foto de acción polémica registrada.", shot.Owner);
        else
            Debug.Log("[RoundObjective] Foto de acción polémica registrada.");

        NetworkRoundExtract extract = NetworkRoundExtract.Instance;
        if (extract == null)
            extract = Object.FindAnyObjectByType<NetworkRoundExtract>();

        if (extract != null)
        {
            PendingNetworkVictoryPhotoRegister = false;
            extract.RegisterVictoryPhotoFromLocal();
        }
        else
            PendingNetworkVictoryPhotoRegister = true;
    }

    public static void ClearPendingNetworkVictoryPhotoRegister()
    {
        PendingNetworkVictoryPhotoRegister = false;
    }

    public static bool HasVictoryPhotoOwnedByOccupant(System.Collections.Generic.IReadOnlyList<PlayerHealth> occupantsInVan)
    {
        if (occupantsInVan == null)
            return false;

        for (int i = 0; i < occupantsInVan.Count; i++)
        {
            PlayerHealth health = occupantsInVan[i];
            if (health == null || health.IsDead)
                continue;
            if (!health.TryGetComponent(out PlayerPhotoAlbum album))
                continue;
            if (album.HasControversialActionShot)
                return true;
        }

        return false;
    }

    public static void Clear()
    {
        HasRegisteredControversialShot = false;
        PendingNetworkVictoryPhotoRegister = false;
    }
}
