using FishNet;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameplayBootstrap : MonoBehaviour
{
    [SerializeField] NetworkManager networkManager;
    [SerializeField] LocalSplitScreenSession localSession;
    [SerializeField] OnlineArenaSpawner onlineArenaSpawner;
    [SerializeField] GameObject celebrityPrefab;
    [SerializeField] NetworkObject networkCelebrityPrefab;
    [SerializeField] CelebritySpawner celebritySpawner;

    // Cacheado: FormatLocalAddressesForDisplay() en OnGUI cada frame mataba al host (~8ms + GC).
    string cachedModeLabel = string.Empty;
    Rect modeLabelRect = new Rect(16f, 0f, 0f, 44f);

    void Start()
    {
        if (GameSessionConfig.Mode == GamePlayMode.None)
        {
            Debug.LogWarning("Gameplay abierto sin modo. Volviendo al menú.");
            SceneManager.LoadScene("MainMenu");
            return;
        }

        RoundResultStore.Clear();
        RoundObjectiveState.Clear();
        DisableSceneCamera();
        EnsureEscapeVehicleExtract();
        EnsureCelebritySpawner();
        EnsureRoundSessionSpawner();
        EnsureAlarmManagerSpawner();
        EnsureAlarmManagerForLocalIfNeeded();

        switch (GameSessionConfig.Mode)
        {
            case GamePlayMode.LocalSplitScreen:
                StartLocal();
                break;
            case GamePlayMode.OnlineHost:
                StartHost();
                break;
            case GamePlayMode.OnlineClient:
                StartClient();
                break;
        }
    }

    void EnsureCelebritySpawner()
    {
        if (celebritySpawner == null)
            celebritySpawner = FindAnyObjectByType<CelebritySpawner>();

        if (celebritySpawner == null)
        {
            var go = new GameObject("CelebritySpawner");
            celebritySpawner = go.AddComponent<CelebritySpawner>();
        }

        if (celebrityPrefab == null)
        {
#if UNITY_EDITOR
            celebrityPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Celebrity.prefab");
#endif
            if (celebrityPrefab == null)
            {
                Debug.LogError(
                    "[GameplayBootstrap] Asigna Celebrity prefab en el inspector " +
                    "(Assets/Prefabs/Celebrity.prefab).");
            }
        }

        if (networkCelebrityPrefab == null)
        {
#if UNITY_EDITOR
            GameObject netGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/NetworkCelebrity.prefab");
            if (netGo != null)
                networkCelebrityPrefab = netGo.GetComponent<NetworkObject>();
#endif
        }

        celebritySpawner.SetLocalPrefab(celebrityPrefab);
        celebritySpawner.SetNetworkPrefab(networkCelebrityPrefab);
    }

    void SpawnCelebrityForSession(bool networked)
    {
        EnsureCelebritySpawner();
        if (celebritySpawner != null)
            celebritySpawner.SpawnAtLevelStart(networked);
    }

    static void EnsureEscapeVehicleExtract()
    {
        GameObject vehicle = null;
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == "EscapeVehicle" || roots[i].name.StartsWith("EscapeVehicle"))
            {
                vehicle = roots[i];
                break;
            }
        }

        if (vehicle != null)
            ArenaBuilder.EnsureEscapeVehicleExtractSetup(vehicle);
    }

    static void EnsureRoundSessionSpawner()
    {
        if (Object.FindAnyObjectByType<RoundSessionSpawner>() != null)
            return;
        var go = new GameObject("RoundSessionSpawner");
        go.AddComponent<RoundSessionSpawner>();
    }

    static void EnsureAlarmManagerSpawner()
    {
        if (Object.FindAnyObjectByType<AlarmManagerSpawner>() != null)
            return;
        var go = new GameObject("AlarmManagerSpawner");
        go.AddComponent<AlarmManagerSpawner>();
    }

    static void EnsureAlarmManagerForLocalIfNeeded()
    {
        // Preferible: AlarmManager colocado en la escena Gameplay (editable).
        if (GameSessionConfig.Mode != GamePlayMode.LocalSplitScreen)
            return;

        if (AlarmManager.Instance != null || Object.FindAnyObjectByType<AlarmManager>() != null)
            return;

        Debug.LogError(
            "[Alarm] No hay AlarmManager en la escena. " +
            "Crea un GameObject vacío 'AlarmManager' en Gameplay y añade el componente AlarmManager.");
    }

    void StartLocal()
    {
        if (networkManager != null)
            networkManager.gameObject.SetActive(false);

        if (onlineArenaSpawner != null)
            onlineArenaSpawner.enabled = false;

        EnsureAlarmManagerForLocalIfNeeded();

        if (localSession == null)
        {
            var go = new GameObject("LocalSplitScreenSession");
            localSession = go.AddComponent<LocalSplitScreenSession>();
        }

        // El prefab se asigna en el Inspector si el session está en escena; si se crea en runtime, carga por path.
        localSession.enabled = true;
        localSession.BeginSession();
        cachedModeLabel = "LOCAL";
        SpawnCelebrityForSession(networked: false);
        // FishNet desactiva NetworkObjects de escena sin red; reactivar trampas/placas offline.
        StartCoroutine(ActivateSecuritySensorsOfflineNextFrame());
    }

    System.Collections.IEnumerator ActivateSecuritySensorsOfflineNextFrame()
    {
        // Tras NetworkObject.Start (que hace SetActive false).
        yield return null;
        TrapController.ActivateAllForOfflinePlay();
        PressurePlateSensor.ActivateAllForOfflinePlay();
        AlarmManager.ActivateAllForOfflinePlay();
    }

    void StartHost()
    {
        if (localSession != null)
            localSession.enabled = false;

        EnsureNetworkManager();
        if (networkManager == null)
            return;

        bool serverStarted = networkManager.ServerManager.StartConnection(GameSessionConfig.DefaultPort);
        bool clientStarted = networkManager.ClientManager.StartConnection();

        string lanInfo = NetworkAddressUtility.FormatLocalAddressesForDisplay();
        cachedModeLabel = $"HOST :{GameSessionConfig.DefaultPort}  |  {lanInfo}";
        if (serverStarted && clientStarted)
        {
            Debug.Log($"Host iniciado en puerto {GameSessionConfig.DefaultPort}. {lanInfo}");
            Debug.Log("Clientes en el mismo PC: 127.0.0.1  ·  Clientes en LAN (portátil): usa la IP de arriba.");
            SpawnCelebrityForSession(networked: true);
        }
        else
        {
            Debug.LogError("No se pudo iniciar el host. Revisa el NetworkManager, puerto 7777 y el firewall de Windows.");
        }
    }

    void StartClient()
    {
        if (localSession != null)
            localSession.enabled = false;

        EnsureNetworkManager();
        if (networkManager == null)
            return;

        networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;

        cachedModeLabel =
            $"CLIENT → {GameSessionConfig.JoinAddress}:{GameSessionConfig.DefaultPort}";

        bool started = networkManager.ClientManager.StartConnection(GameSessionConfig.JoinAddress, GameSessionConfig.DefaultPort);
        Debug.Log(started
            ? $"Cliente conectando a {GameSessionConfig.JoinAddress}:{GameSessionConfig.DefaultPort}..."
            : "No se pudo iniciar el cliente.");
    }

    void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            Debug.Log("Cliente conectado al host.");
            // El famoso lo spawnea solo el server; aquí llega por réplica FishNet.
        }
        else if (args.ConnectionState == LocalConnectionState.Stopped)
            Debug.LogWarning("Cliente desconectado / no pudo conectar. Comprueba IP, puerto, firewall y que el host esté en marcha.");
    }

    void EnsureNetworkManager()
    {
        if (networkManager == null)
            networkManager = InstanceFinder.NetworkManager;

        if (networkManager == null)
            networkManager = FindAnyObjectByType<NetworkManager>();

        if (networkManager == null)
        {
            Debug.LogError("No hay NetworkManager en la escena Gameplay. Ejecuta Tools > Coop Prototype > Setup Prefabs And Scenes.");
            enabled = false;
            return;
        }

        networkManager.gameObject.SetActive(true);
    }

    void DisableSceneCamera()
    {
        Camera main = Camera.main;
        if (main == null)
            return;

        main.enabled = false;
        if (main.TryGetComponent(out AudioListener listener))
            listener.enabled = false;
        main.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (networkManager != null)
            networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
    }

    void OnGUI()
    {
        if (cachedModeLabel.Length == 0)
            return;

        modeLabelRect.y = Screen.height - 56f;
        modeLabelRect.width = Screen.width - 32f;
        GUI.Label(modeLabelRect, cachedModeLabel);
    }
}

