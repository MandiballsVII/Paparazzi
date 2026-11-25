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
    const float OnlineLobbyWaitTimeoutSeconds = 8f;

    static bool extractInProgress;

    public static bool IsExtractInProgress => extractInProgress;

    /// <summary>
    /// Offline: carga Lobby local.
    /// Online server: LoadGlobalScenes (seguro también si se llama por error en host).
    /// Online client: espera a que el server cargue Lobby.
    /// </summary>
    public static void ExtractToLobby()
    {
        if (extractInProgress)
            return;

        bool online = IsOnlineNetworkSession()
                      && GameSessionConfig.Mode != GamePlayMode.LocalSplitScreen;
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (online && nm != null && nm.IsServerStarted)
        {
            ServerLoadLobbyNow();
            return;
        }

        extractInProgress = true;
        RoundExtractButton.NotifyExtractStatus("Finalizando ronda...", sticky: true);
        StartWaitRunner();
    }

    /// <summary>
    /// Solo server: LoadGlobalScenes inmediato (no depende de corrutina ni de recibir ObserversRpc).
    /// </summary>
    public static void ServerLoadLobbyNow()
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null || !nm.IsServerStarted)
        {
            Debug.LogError("[Extract] ServerLoadLobbyNow llamado sin server activo.");
            return;
        }

        bool alreadyWaiting = extractInProgress;
        extractInProgress = true;
        RoundExtractButton.NotifyExtractStatus("Finalizando ronda...", sticky: true);
        RoundObjectiveState.Clear();

        Debug.Log("[Extract] ServerLoadLobbyNow → LoadGlobalScenes");
        LoadLobbyAsGlobalSceneMovingPlayers();

        // Waiter DDOL: limpia flag / permite reintento si falla.
        if (!alreadyWaiting)
            StartWaitRunner();
    }

    static void StartWaitRunner()
    {
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
                LobbyPlayerTransfer.PrepareBeforeLeaveGameplay();
                yield return null;
                StopAndDestroyNetworkManager();
                yield return null;
                UnityEngine.SceneManagement.SceneManager.LoadScene(LobbySceneName);
                extractInProgress = false;
                Destroy(gameObject);
                yield break;
            }

            // Online: solo espera Lobby (el load lo hace ServerLoadLobbyNow en el server).
            float elapsed = 0f;
            while (!IsLobbySceneLoaded() && elapsed < OnlineLobbyWaitTimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!IsLobbySceneLoaded())
            {
                Debug.LogWarning("[Extract] Timeout esperando escena Lobby en red.");
                NetworkRoundExtract.ClearExtractCommitIfPresent();
                RoundExtractButton.NotifyExtractStatus(
                    "Error al ir al Lobby. Inténtalo de nuevo.",
                    sticky: false);
            }

            extractInProgress = false;
            Destroy(gameObject);
        }
    }
}
