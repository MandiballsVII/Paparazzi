using FishNet;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Online: cada owner reporta su álbum al server para liquidar dinero compartido.
/// Debe existir en el prefab NetworkPlayer antes del spawn (NetworkBehaviour).
/// </summary>
public class PhotoEconomyReporter : NetworkBehaviour
{
    /// <returns>False si aún no se puede reportar (retry).</returns>
    public bool SubmitAlbumForSettlement()
    {
        if (!TryGetComponent(out PlayerPhotoAlbum album))
        {
            Debug.LogWarning(
                $"[Economy] Submit sin PlayerPhotoAlbum go={name} id={GetInstanceID()}");
            return true;
        }

        RunEconomy.CountAlbum(album, out int controversial, out int famous);
        LogAlbumSnapshot("Submit", album, controversial, famous);

        if (!IsNetworkSessionActive())
            return true;

        if (!TryGetComponent(out NetworkObject nob) || nob == null || !nob.IsSpawned)
        {
            Debug.Log(
                $"[Economy] Submit retry: NetworkObject no listo go={name} id={GetInstanceID()}");
            return false;
        }

        if (IsServerInitialized)
        {
            NetworkConnection contributor = Owner;
            if (contributor == null && NetworkManager != null && NetworkManager.ClientManager != null)
                contributor = NetworkManager.ClientManager.Connection;

            Debug.Log(
                $"[Economy] Submit via SERVER local go={name} id={GetInstanceID()} " +
                $"nob={nob.ObjectId} ownerClient={(contributor != null ? contributor.ClientId : -1)} " +
                $"c={controversial} f={famous}");

            AcceptContribution(contributor, controversial, famous);
            return true;
        }

        if (IsClientInitialized && IsOwner)
        {
            Debug.Log(
                $"[Economy] Submit via ServerRpc go={name} id={GetInstanceID()} " +
                $"nob={nob.ObjectId} c={controversial} f={famous}");
            ReportAlbumServerRpc(controversial, famous);
            return true;
        }

        Debug.Log(
            $"[Economy] Submit ignorado (no server/owner) go={name} id={GetInstanceID()} " +
            $"server={IsServerInitialized} client={IsClientInitialized} owner={IsOwner}");
        return true;
    }

    [ServerRpc]
    void ReportAlbumServerRpc(int controversialCount, int famousCount, NetworkConnection caller = null)
    {
        Debug.Log(
            $"[Economy] ServerRpc recibido go={name} id={GetInstanceID()} " +
            $"caller={(caller != null ? caller.ClientId : -1)} c={controversialCount} f={famousCount}");
        AcceptContribution(caller ?? Owner, controversialCount, famousCount);
    }

    void AcceptContribution(NetworkConnection contributor, int controversial, int famous)
    {
        if (!IsServerInitialized)
            return;

        int contributorId = contributor != null ? contributor.ClientId : -1;
        int expected = CountExpectedContributors();

        Debug.Log(
            $"[Economy] AcceptContribution contributor={contributorId} c={controversial} f={famous} " +
            $"expected={expected}");

        if (!RunEconomy.TryAddOnlineContribution(
                contributorId,
                controversial,
                famous,
                expected,
                out RunSettlement settlement))
        {
            Debug.Log(
                $"[Economy] Contribution pendiente o duplicada contributor={contributorId}");
            return;
        }

        Debug.Log(
            $"[Economy] Settlement FINAL c={settlement.ControversialCount} f={settlement.FamousCount} " +
            $"run={settlement.RunTotal} accum={settlement.AccumulatedBefore} total={settlement.GrandTotal}");

        NewsBoardPresenter.RefreshAll();
        BroadcastSettlementObserversRpc(
            settlement.ControversialCount,
            settlement.FamousCount,
            settlement.RunTotal,
            settlement.AccumulatedBefore,
            settlement.GrandTotal);
    }

    static int CountExpectedContributors()
    {
        int count = 0;
        NetworkPlayerController[] players =
            Object.FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            NetworkPlayerController player = players[i];
            if (player == null)
                continue;
            if (!player.TryGetComponent(out NetworkObject nob) || !nob.IsSpawned)
                continue;
            count++;
        }

        return Mathf.Max(1, count);
    }

    [ObserversRpc]
    void BroadcastSettlementObserversRpc(
        int controversial,
        int famous,
        int runTotal,
        int accumulatedBefore,
        int grandTotal)
    {
        var settlement = new RunSettlement
        {
            ControversialCount = controversial,
            FamousCount = famous,
            RunTotal = runTotal,
            AccumulatedBefore = accumulatedBefore,
            GrandTotal = grandTotal
        };
        settlement.BuildBreakdownLines();

        Debug.Log(
            $"[Economy] Broadcast settlement recibido server={IsServerInitialized} " +
            $"c={controversial} f={famous} run={runTotal} total={grandTotal}");

        if (!IsServerInitialized)
            RunEconomy.ApplyRemoteSettlement(settlement);

        NewsBoardPresenter.RefreshAll();
    }

    void LogAlbumSnapshot(string tag, PlayerPhotoAlbum album, int controversial, int famous)
    {
        int zeroValue = 0;
        int missingEval = 0;
        if (album != null)
        {
            for (int i = 0; i < album.Count; i++)
            {
                PhotoShot shot = album.Shots[i];
                if (shot == null || shot.Evaluation == null)
                {
                    missingEval++;
                    continue;
                }

                if (PhotoEvaluator.GetPayoutValue(shot.Evaluation) <= 0)
                    zeroValue++;
            }
        }

        Debug.Log(
            $"[Economy] {tag} go={name} id={GetInstanceID()} scene={gameObject.scene.name} " +
            $"albumId={(album != null ? album.GetInstanceID() : 0)} shots={album?.Count ?? 0} " +
            $"payC={controversial} payF={famous} zeroValue={zeroValue} missingEval={missingEval} " +
            $"server={IsServerInitialized} client={IsClientInitialized} owner={IsOwner}");
    }

    static bool IsNetworkSessionActive()
    {
        var nm = InstanceFinder.NetworkManager;
        return nm != null && (nm.IsServerStarted || nm.IsClientStarted);
    }
}
