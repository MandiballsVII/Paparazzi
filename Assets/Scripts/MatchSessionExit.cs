using System.Collections;
using FishNet;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MatchSessionExit
{
    const string MainMenuSceneName = "MainMenu";
    const string LobbySceneName = "Lobby";

    /// <summary>
    /// Tiempo para que el ObserversRpc de extract llegue a clients antes de tumbar el listen-server.
    /// </summary>
    const float HostExtractNetworkFlushSeconds = 0.4f;
    const int HostExtractFlushFrames = 3;

    static bool extractInProgress;

    public static bool IsExtractInProgress => extractInProgress;

    public static void ExtractToLobby()
    {
        if (extractInProgress)
            return;

        extractInProgress = true;

        var runnerGo = new GameObject("ExtractToLobbyRunner");
        Object.DontDestroyOnLoad(runnerGo);
        runnerGo.AddComponent<ExtractToLobbyRunner>().Begin();
    }

    public static void LeaveMatch()
    {
        extractInProgress = false;
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
        extractInProgress = false;
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

            // Listen-server: no cortar la red en el mismo frame que el ObserversRpc;
            // si no, el client puede no recibirlo y quedarse bloqueado en Gameplay.
            NetworkManager networkManager = InstanceFinder.NetworkManager;
            bool isListenServer = networkManager != null && networkManager.IsServerStarted;
            if (isListenServer)
            {
                for (int i = 0; i < HostExtractFlushFrames; i++)
                    yield return null;

                yield return new WaitForSecondsRealtime(HostExtractNetworkFlushSeconds);
            }
            else
            {
                yield return null;
            }

            StopAndDestroyNetworkManager();
            yield return null;

            SceneManager.LoadScene(LobbySceneName);
            extractInProgress = false;
            Destroy(gameObject);
        }
    }
}
