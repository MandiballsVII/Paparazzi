using FishNet;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MatchSessionExit
{
    const string MainMenuSceneName = "MainMenu";
    const string LobbySceneName = "Lobby";

    public static void ExtractToLobby()
    {
        // 1) Preservar jugadores (clon online / DontDestroyOnLoad local) ANTES de parar red.
        LobbyPlayerTransfer.PrepareBeforeLeaveGameplay();

        // 2) Parar FishNet (destruye NetworkObjects de Gameplay; los preservados ya están fuera).
        StopNetworking();

        RoundObjectiveState.Clear();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 3) Cargar Lobby (LobbyBootstrap coloca players + UI).
        SceneManager.LoadScene(LobbySceneName);
    }

    public static void LeaveMatch()
    {
        LobbyPlayerTransfer.DestroyPreservedPlayers();
        StopNetworking();
        RoundResultStore.Clear();
        RoundObjectiveState.Clear();
        GameSessionConfig.Clear();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(MainMenuSceneName);
    }

    static void StopNetworking()
    {
        NetworkManager networkManager = InstanceFinder.NetworkManager;
        if (networkManager == null)
            networkManager = Object.FindAnyObjectByType<NetworkManager>();

        if (networkManager != null)
        {
            if (networkManager.ServerManager != null && networkManager.IsServerStarted)
                networkManager.ServerManager.StopConnection(true);

            if (networkManager.ClientManager != null && networkManager.IsClientStarted)
                networkManager.ClientManager.StopConnection();
        }
    }

    public static void QuitGame()
    {
        LobbyPlayerTransfer.DestroyPreservedPlayers();
        GameSessionConfig.Clear();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
