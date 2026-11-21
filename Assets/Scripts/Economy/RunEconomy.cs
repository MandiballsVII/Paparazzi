using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dinero compartido de la run (local + online). Persiste hasta LeaveMatch / Clear.
/// </summary>
public static class RunEconomy
{
    static readonly HashSet<int> ReportedContributorIds = new();
    static int pendingControversial;
    static int pendingFamous;
    static bool settlementBroadcast;

    public static int Balance { get; private set; }
    public static RunSettlement LastSettlement { get; private set; }
    public static bool HasSettlement => LastSettlement != null;

    public static void Clear()
    {
        Balance = 0;
        LastSettlement = null;
        ResetPendingSettlement();
    }

    public static void ResetPendingSettlement()
    {
        ReportedContributorIds.Clear();
        pendingControversial = 0;
        pendingFamous = 0;
        settlementBroadcast = false;
    }

    /// <summary>Local: liquida todos los álbumes visibles y actualiza Balance.</summary>
    public static RunSettlement SettleFromAllLocalAlbums()
    {
        CountAllAlbums(out int controversial, out int famous);
        return FinalizeSettlement(controversial, famous);
    }

    /// <summary>
    /// Online server: acumula aportación por jugador (ClientId).
    /// Devuelve settlement si ya han reportado todos los esperados.
    /// </summary>
    public static bool TryAddOnlineContribution(
        int contributorId,
        int controversialCount,
        int famousCount,
        int expectedContributors,
        out RunSettlement settlement)
    {
        settlement = null;
        if (settlementBroadcast)
            return false;

        if (!ReportedContributorIds.Add(contributorId))
            return false;

        pendingControversial += Mathf.Max(0, controversialCount);
        pendingFamous += Mathf.Max(0, famousCount);

        if (ReportedContributorIds.Count < Mathf.Max(1, expectedContributors))
            return false;

        settlement = FinalizeSettlement(pendingControversial, pendingFamous);
        settlementBroadcast = true;
        return true;
    }

    public static void ApplyRemoteSettlement(RunSettlement settlement)
    {
        if (settlement == null)
            return;

        LastSettlement = settlement;
        Balance = settlement.GrandTotal;
    }

    public static void CountAlbum(PlayerPhotoAlbum album, out int controversial, out int famous)
    {
        controversial = 0;
        famous = 0;
        if (album == null)
            return;

        for (int i = 0; i < album.Count; i++)
        {
            PhotoShot shot = album.Shots[i];
            if (shot == null || shot.Evaluation == null)
                continue;

            int value = PhotoEvaluator.GetPayoutValue(shot.Evaluation);
            if (value <= 0)
                continue;

            if (shot.IsControversialActionShot)
                controversial++;
            else
                famous++;
        }
    }

    static void CountAllAlbums(out int controversial, out int famous)
    {
        controversial = 0;
        famous = 0;
        PlayerPhotoAlbum[] albums = Object.FindObjectsByType<PlayerPhotoAlbum>(FindObjectsSortMode.None);
        for (int i = 0; i < albums.Length; i++)
        {
            CountAlbum(albums[i], out int c, out int f);
            controversial += c;
            famous += f;
        }
    }

    static RunSettlement FinalizeSettlement(int controversial, int famous)
    {
        int runTotal =
            controversial * RunEconomyConstants.ControversialPhotoValue
            + famous * RunEconomyConstants.FamousPhotoValue;

        int accumulated = Balance;
        var settlement = new RunSettlement
        {
            ControversialCount = controversial,
            FamousCount = famous,
            RunTotal = runTotal,
            AccumulatedBefore = accumulated,
            GrandTotal = accumulated + runTotal
        };
        settlement.BuildBreakdownLines();

        LastSettlement = settlement;
        Balance = settlement.GrandTotal;
        return settlement;
    }
}
