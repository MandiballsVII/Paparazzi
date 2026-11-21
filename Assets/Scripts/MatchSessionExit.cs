using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MatchSessionExit
{
    const string MainMenuSceneName = "MainMenu";
    const string LobbySceneName = "Lobby";

    /// <summary>
    /// Tiempo para que el ObserversRpc de extract llegue a clients antes de cargar escenas (host).
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
        RunEconomy.Clear();
        GameSessionConfig.Clear();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene(MainMenuSceneName);
    }

    public static void QuitGame()
    {
        extractInProgress = false;
        LobbyPlayerTransfer.DestroyPreservedPlayers();
        RunEconomy.Clear();
        GameSessionConfig.Clear();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    static bool IsOnlineNetworkSession()
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null)
            return false;
        return nm.IsServerStarted || nm.IsClientStarted;
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

    static NetworkObject[] CollectSpawnedPlayerNetworkObjects()
    {
        var list = new List<NetworkObject>(8);
        NetworkPlayerController[] players =
            Object.FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);

        for (int i = 0; i < players.Length; i++)
        {
            NetworkPlayerController player = players[i];
            if (player == null)
                continue;
            if (!player.TryGetComponent(out NetworkObject nob) || !nob.IsSpawned)
                continue;

            int shots = 0;
            int albumId = 0;
            if (player.TryGetComponent(out PlayerPhotoAlbum album) && album != null)
            {
                shots = album.Count;
                albumId = album.GetInstanceID();
            }

            Debug.Log(
                $"[Extract] Move candidate go={player.name} id={player.GetInstanceID()} " +
                $"nob={nob.ObjectId} scene={player.gameObject.scene.name} " +
                $"albumId={albumId} shots={shots} owner={player.IsOwner}");

            list.Add(nob);
        }

        return list.ToArray();
    }

    static void LoadLobbyAsGlobalSceneMovingPlayers()
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null || nm.SceneManager == null)
        {
            Debug.LogError("[Extract] No NetworkManager/SceneManager para cargar Lobby en red.");
            return;
        }

        NetworkObject[] players = CollectSpawnedPlayerNetworkObjects();
        SceneLoadData sld = new SceneLoadData(LobbySceneName)
        {
            ReplaceScenes = ReplaceOption.All,
            MovedNetworkObjects = players,
            PreferredActiveScene = new PreferredScene(new SceneLookupData(LobbySceneName))
        };

        Debug.Log($"[Extract] LoadGlobalScenes Lobby (players moved={players.Length}).");
        nm.SceneManager.LoadGlobalScenes(sld);
    }

    static bool IsLobbySceneLoaded()
    {
        Scene lobby = UnityEngine.SceneManagement.SceneManager.GetSceneByName(LobbySceneName);
        return lobby.IsValid() && lobby.isLoaded;
    }

    sealed class ExtractToLobbyRunner : MonoBehaviour
    {
        public void Begin() => StartCoroutine(Run());

        IEnumerator Run()
        {
            RoundObjectiveState.Clear();

            bool online = IsOnlineNetworkSession()
                          && GameSessionConfig.Mode != GamePlayMode.LocalSplitScreen;

            if (!online)
            {
                // Local / offline: DDOL players + Unity LoadScene (sin red).
                LobbyPlayerTransfer.PrepareBeforeLeaveGameplay();
                yield return null;
                StopAndDestroyNetworkManager();
                yield return null;
                UnityEngine.SceneManagement.SceneManager.LoadScene(LobbySceneName);
                extractInProgress = false;
                Destroy(gameObject);
                yield break;
            }

            // Online: mantener FishNet. Solo el server carga Lobby global;
            // clientes reciben la escena por SceneManager.
            NetworkManager networkManager = InstanceFinder.NetworkManager;
            bool isServer = networkManager != null && networkManager.IsServerStarted;

            if (isServer)
            {
                for (int i = 0; i < HostExtractFlushFrames; i++)
                    yield return null;

                yield return new WaitForSecondsRealtime(HostExtractNetworkFlushSeconds);
                LoadLobbyAsGlobalSceneMovingPlayers();
            }

            float timeout = 15f;
            float elapsed = 0f;
            while (!IsLobbySceneLoaded() && elapsed < timeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!IsLobbySceneLoaded())
                Debug.LogWarning("[Extract] Timeout esperando escena Lobby en red.");

            extractInProgress = false;
            Destroy(gameObject);
        }
    }
}
