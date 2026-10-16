using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameplayBootstrap : MonoBehaviour
{
    [SerializeField] NetworkManager networkManager;
    [SerializeField] LocalSplitScreenSession localSession;
    [SerializeField] OnlineArenaSpawner onlineArenaSpawner;

    void Start()
    {
        if (GameSessionConfig.Mode == GamePlayMode.None)
        {
            Debug.LogWarning("Gameplay abierto sin modo. Volviendo al menú.");
            SceneManager.LoadScene("MainMenu");
            return;
        }

        DisableSceneCamera();

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
    }

    void StartHost()
    {
        if (localSession != null)
            localSession.enabled = false;

        EnsureNetworkManager();
        if (networkManager == null)
            return;

        onlineArenaSpawner?.EnsureStaticArena();

        bool serverStarted = networkManager.ServerManager.StartConnection(GameSessionConfig.DefaultPort);
        bool clientStarted = networkManager.ClientManager.StartConnection();

        string lanInfo = NetworkAddressUtility.FormatLocalAddressesForDisplay();
        if (serverStarted && clientStarted)
        {
            Debug.Log($"Host iniciado en puerto {GameSessionConfig.DefaultPort}. {lanInfo}");
            Debug.Log("Clientes en el mismo PC: 127.0.0.1  ·  Clientes en LAN (portátil): usa la IP de arriba.");
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

        onlineArenaSpawner?.EnsureStaticArena();
        networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;

        bool started = networkManager.ClientManager.StartConnection(GameSessionConfig.JoinAddress, GameSessionConfig.DefaultPort);
        Debug.Log(started
            ? $"Cliente conectando a {GameSessionConfig.JoinAddress}:{GameSessionConfig.DefaultPort}..."
            : "No se pudo iniciar el cliente.");
    }

    void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
            Debug.Log("Cliente conectado al host.");
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
        if (GameSessionConfig.Mode == GamePlayMode.None)
            return;

        string modeLabel = GameSessionConfig.Mode switch
        {
            GamePlayMode.LocalSplitScreen => "LOCAL",
            GamePlayMode.OnlineHost => $"HOST :{GameSessionConfig.DefaultPort}  |  {NetworkAddressUtility.FormatLocalAddressesForDisplay()}",
            GamePlayMode.OnlineClient => $"CLIENT → {GameSessionConfig.JoinAddress}:{GameSessionConfig.DefaultPort}",
            _ => ""
        };

        GUI.Label(new Rect(16f, Screen.height - 56f, Screen.width - 32f, 44f), modeLabel);
    }
}

