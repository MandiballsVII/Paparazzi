using System.Collections;
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
        var runnerGo = new GameObject("ExtractToLobbyRunner");
        Object.DontDestroyOnLoad(runnerGo);
        runnerGo.AddComponent<ExtractToLobbyRunner>().Begin();
    }

    public static void LeaveMatch()
    {
        LobbyPlayerTransfer.DestroyPreservedPlayers();
        StopAndDestroyNetworkManager();
        RoundResultStore.Clear();
        RoundObjectiveState.Clear();
        GameSessionConfig.Clear();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(MainMenuSceneName);
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

    static void StopAndDestroyNetworkManager()
    {
        NetworkManager networkManager = InstanceFinder.NetworkManager;
        if (networkManager == null)
            networkManager = Object.FindAnyObjectByType<NetworkManager>();

        if (networkManager == null)
            return;

        if (networkManager.ServerManager != null && networkManager.IsServerStarted)
            networkManager.ServerManager.StopConnection(true);

        if (networkManager.ClientManager != null && networkManager.IsClientStarted)
            networkManager.ClientManager.StopConnection();

        Object.Destroy(networkManager.gameObject);
    }

    sealed class ExtractToLobbyRunner : MonoBehaviour
    {
        public void Begin() => StartCoroutine(Run());

        IEnumerator Run()
        {
            LobbyPlayerTransfer.PrepareBeforeLeaveGameplay();
            RoundObjectiveState.Clear();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            StopAndDestroyNetworkManager();
            yield return null;

            SceneManager.LoadScene(LobbySceneName);
            Destroy(gameObject);
        }
    }
}
