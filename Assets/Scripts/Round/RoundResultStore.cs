public static class RoundResultStore
{
    public static bool HasResult { get; private set; }
    public static bool IsVictory { get; private set; }
    public static string Message { get; private set; } = string.Empty;

    public static void SetExtractResult(bool victory)
    {
        HasResult = true;
        IsVictory = victory;
        Message = victory
            ? "VICTORIA\nHabéis conseguido la foto de la acción polémica."
            : "EXTRACCIÓN\nHabéis huido sin la foto de la acción polémica.";
    }

    public static void Clear()
    {
        HasResult = false;
        IsVictory = false;
        Message = string.Empty;
    }
}
