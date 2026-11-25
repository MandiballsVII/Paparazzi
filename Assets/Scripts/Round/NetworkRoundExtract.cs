using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Extract sincronizado: valida en server y manda a todos al Lobby.
/// </summary>
public class NetworkRoundExtract : NetworkBehaviour
{
    public static NetworkRoundExtract Instance { get; private set; }

    readonly SyncVar<bool> syncedHasVictoryPhoto = new();

    [SerializeField]
    VehicleInteriorZone interiorZone;

    bool extractCommitted;

    public bool HasVictoryPhoto =>
        IsNetworkSessionActive()
            ? syncedHasVictoryPhoto.Value
            : RoundObjectiveState.HasRegisteredControversialShot;

    void Awake()
    {
        Instance = this;
        if (interiorZone == null)
            interiorZone = FindAnyObjectByType<VehicleInteriorZone>();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if (RoundObjectiveState.HasRegisteredControversialShot
            || RoundObjectiveState.PendingNetworkVictoryPhotoRegister)
        {
            RegisterVictoryPhotoFromLocal();
            RoundObjectiveState.ClearPendingNetworkVictoryPhotoRegister();
        }
    }

    public void RequestExtractFrom(GameObject interactor, out string failReason)
    {
        failReason = string.Empty;

        if (!IsNetworkSessionActive())
        {
            if (!ValidateOccupancy(out failReason, out bool victory))
                return;
            RoundResultStore.SetExtractResult(victory);
            MatchSessionExit.ExtractToLobby();
            return;
        }

        if (IsServerInitialized)
        {
            if (extractCommitted || MatchSessionExit.IsExtractInProgress)
                return;

            if (!ValidateOccupancy(out failReason, out bool victory))
            {
                ExtractFailedObserversRpc(failReason);
                return;
            }

            CommitExtract(victory);
            return;
        }

        if (extractCommitted || MatchSessionExit.IsExtractInProgress)
            return;

        RequestExtractServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestExtractServerRpc(FishNet.Connection.NetworkConnection caller = null)
    {
        if (extractCommitted || MatchSessionExit.IsExtractInProgress)
            return;

        if (!ValidateOccupancy(out string failReason, out bool victory))
        {
            ExtractFailedObserversRpc(failReason);
            return;
        }

        CommitExtract(victory);
    }

    void CommitExtract(bool victory)
    {
        if (extractCommitted)
            return;

        extractCommitted = true;
        RoundResultStore.SetExtractResult(victory);

        Debug.Log(
            $"[RoundExtract] CommitExtract victory={victory} " +
            $"extractInProgress={MatchSessionExit.IsExtractInProgress} " +
            $"isServer={IsServerInitialized}");

        // Remotos: UI + espera Lobby. ExcludeServer: el host no debe entrar por ObserversRpc
        // (el load lo hace el server de forma explícita justo después).
        BeginExtractObserversRpc(victory);

        // Load inmediato en server (funciona igual si el commit viene de Interact o de ServerRpc).
        MatchSessionExit.ServerLoadLobbyNow();
    }

    [ObserversRpc(ExcludeServer = true)]
    void BeginExtractObserversRpc(bool victory)
    {
        extractCommitted = true;
        RoundResultStore.SetExtractResult(victory);
        RoundExtractButton.NotifyExtractStatus("Finalizando ronda...", sticky: true);
        MatchSessionExit.ExtractToLobby();
    }

    [ObserversRpc]
    void ExtractFailedObserversRpc(string reason)
    {
        RoundExtractButton.NotifyExtractStatus(reason, sticky: false);
    }

    /// <summary>Tras timeout de Lobby: permitir reintentar el botón de extract.</summary>
    public void ClearExtractCommit()
    {
        extractCommitted = false;
    }

    public static void ClearExtractCommitIfPresent()
    {
        if (Instance != null)
            Instance.ClearExtractCommit();
    }

    bool ValidateOccupancy(out string failReason, out bool victory)
    {
        failReason = string.Empty;
        victory = false;

        if (interiorZone == null)
            interiorZone = FindAnyObjectByType<VehicleInteriorZone>();

        if (interiorZone == null)
        {
            failReason = "Zona de la furgoneta no encontrada.";
            return false;
        }

        IReadOnlyList<PlayerHealth> inVan = interiorZone.GetLivingOccupants();
        PlayerHealth[] all = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        int livingTotal = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && !all[i].IsDead)
                livingTotal++;
        }

        if (livingTotal <= 0)
        {
            failReason = "No hay jugadores vivos.";
            return false;
        }

        if (inVan.Count < livingTotal)
        {
            failReason = $"Faltan jugadores vivos en la furgoneta ({inVan.Count}/{livingTotal}).";
            return false;
        }

        victory = HasVictoryPhoto || RoundObjectiveState.HasVictoryPhotoOwnedByOccupant(inVan);
        return true;
    }

    public void RegisterVictoryPhotoFromLocal()
    {
        if (!IsNetworkSessionActive())
            return;

        if (IsServerInitialized)
        {
            syncedHasVictoryPhoto.Value = true;
            return;
        }

        RegisterVictoryPhotoServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    void RegisterVictoryPhotoServerRpc()
    {
        syncedHasVictoryPhoto.Value = true;
    }

    static bool IsNetworkSessionActive()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null)
            return false;
        return nm.IsServerStarted || nm.IsClientStarted;
    }
}
