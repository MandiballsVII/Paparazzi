public enum GamePlayMode
{
    None,
    LocalSplitScreen,
    OnlineHost,
    OnlineClient
}

public static class GameSessionConfig
{
    public static GamePlayMode Mode { get; private set; } = GamePlayMode.None;
    public static string JoinAddress { get; private set; } = "127.0.0.1";
    public const ushort DefaultPort = 7777;

    public static void SetLocal()
    {
        Mode = GamePlayMode.LocalSplitScreen;
        JoinAddress = "127.0.0.1";
    }

    public static void SetHost()
    {
        Mode = GamePlayMode.OnlineHost;
        JoinAddress = "127.0.0.1";
    }

    public static void SetClient(string address)
    {
        Mode = GamePlayMode.OnlineClient;
        JoinAddress = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
    }

    public static void Clear()
    {
        Mode = GamePlayMode.None;
    }
}
