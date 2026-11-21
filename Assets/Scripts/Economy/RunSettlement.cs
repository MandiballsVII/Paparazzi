using System.Collections.Generic;

/// <summary>
/// Resultado de liquidar fotos al llegar al lobby.
/// </summary>
public sealed class RunSettlement
{
    public int ControversialCount { get; set; }
    public int FamousCount { get; set; }
    public int RunTotal { get; set; }
    public int AccumulatedBefore { get; set; }
    public int GrandTotal { get; set; }

    /// <summary>Líneas listas para UI (sin título de sección).</summary>
    public List<string> BreakdownLines { get; } = new();

    public void BuildBreakdownLines()
    {
        BreakdownLines.Clear();

        if (ControversialCount > 0)
        {
            int amount = ControversialCount * RunEconomyConstants.ControversialPhotoValue;
            BreakdownLines.Add(FormatGroupedLine(
                RunEconomyConstants.ControversialPhotoLabel,
                ControversialCount,
                amount));
        }

        if (FamousCount > 0)
        {
            int amount = FamousCount * RunEconomyConstants.FamousPhotoValue;
            BreakdownLines.Add(FormatGroupedLine(
                RunEconomyConstants.FamousPhotoLabel,
                FamousCount,
                amount));
        }

        BreakdownLines.Add($"{RunEconomyConstants.RunTotalLabel} -> {RunTotal}");
        BreakdownLines.Add($"{RunEconomyConstants.AccumulatedLabel} -> {AccumulatedBefore}");
        BreakdownLines.Add($"{RunEconomyConstants.GrandTotalLabel} -> {GrandTotal}");
    }

    static string FormatGroupedLine(string label, int count, int amount)
    {
        if (count <= 1)
            return $"{label} -> {amount}";
        return $"{label} x{count} -> {amount}";
    }
}

public static class RunEconomyConstants
{
    public const int ControversialPhotoValue = 1000;
    public const int FamousPhotoValue = 50;

    public const string ControversialPhotoLabel = "Foto polémica";
    public const string FamousPhotoLabel = "Foto famoso";
    public const string SectionTitle = "Dinero conseguido";
    public const string RunTotalLabel = "Total de la run";
    public const string AccumulatedLabel = "Dinero acumulado";
    public const string GrandTotalLabel = "Total";
}
