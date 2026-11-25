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
    [SerializeField] LevelManager levelManager;

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
        ResolveLevelManager();

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

    void ResolveLevelManager()
    {
        if (levelManager == null)
            levelManager = LevelManager.Instance;
        if (levelManager == null)
            levelManager = FindAnyObjectByType<LevelManager>(FindObjectsInactive.Include);

        if (levelManager == null)
        {
            Debug.LogError(
                "[GameplayBootstrap] No hay LevelManager en la escena. " +
                "Tools > Paparazzi > Place Level Manager In Gameplay.");
        }
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

    void StartLocal()
    {
        if (networkManager != null)
            networkManager.gameObject.SetActive(false);

        if (onlineArenaSpawner != null)
            onlineArenaSpawner.enabled = false;

        if (localSession == null)
        {
            var go = new GameObject("LocalSplitScreenSession");
            localSession = go.AddComponent<LocalSplitScreenSession>();
        }

        localSession.enabled = true;
        localSession.BeginSession();
        cachedModeLabel = "LOCAL";

        if (levelManager != null)
            levelManager.InitializeLocal();
        else
            Debug.LogError("[GameplayBootstrap] Local sin LevelManager: no hay RoundSession/Alarm/Celebrity.");

        // FishNet desactiva NetworkObjects de escena sin red; reactivar seguridad offline.
        StartCoroutine(ActivateSecuritySensorsOfflineNextFrame());
    }

    System.Collections.IEnumerator ActivateSecuritySensorsOfflineNextFrame()
    {
        yield return null;
        if (levelManager != null)
            levelManager.ActivateNetworkedChildrenForOffline();
        TrapController.ActivateAllForOfflinePlay();
        PressurePlateSensor.ActivateAllForOfflinePlay();
        AlarmManager.ActivateAllForOfflinePlay();
        SurveillanceCamera.ActivateAllForOfflinePlay();
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
            if (levelManager != null)
                levelManager.InitializeHost();
            else
                Debug.LogError("[GameplayBootstrap] Host sin LevelManager.");
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

        if (levelManager != null)
            levelManager.InitializeClient();

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
