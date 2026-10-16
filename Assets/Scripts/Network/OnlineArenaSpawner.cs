using FishNet;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

public class OnlineArenaSpawner : MonoBehaviour
{
    [SerializeField]
    NetworkObject physicsBoxPrefab;

    [SerializeField]
    NetworkObject physicsSpherePrefab;

    bool spawned;
    GameObject staticArena;
    NetworkManager networkManager;

    public void EnsureStaticArena()
    {
        if (staticArena == null)
            staticArena = ArenaBuilder.BuildStaticArena();
    }

    void OnEnable()
    {
        networkManager = InstanceFinder.NetworkManager;
        if (networkManager == null)
            networkManager = FindAnyObjectByType<NetworkManager>();

        if (networkManager != null)
            networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
    }

    void OnDisable()
    {
        if (networkManager != null)
            networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
    }

    void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
            SpawnNetworkPropsIfServer();
    }

    public void SpawnNetworkPropsIfServer()
    {
        if (spawned)
            return;

        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null || !nm.IsServerStarted)
            return;

        if (physicsBoxPrefab == null || physicsSpherePrefab == null)
        {
            Debug.LogError(
                "Faltan prefabs de físicas de red. Ejecuta Tools > Coop Prototype > Setup Prefabs And Scenes."
            );
            return;
        }

        EnsureStaticArena();

        SpawnBox(new Vector3(-2f, 0.6f, 0f), Vector3.one, 2.5f, new Color(0.85f, 0.45f, 0.15f));
        SpawnBox(
            new Vector3(0f, 0.6f, -1.5f),
            new Vector3(1.2f, 1.2f, 1.2f),
            4f,
            new Color(0.2f, 0.6f, 0.8f)
        );
        SpawnBox(
            new Vector3(2.2f, 0.4f, 0.4f),
            new Vector3(0.8f, 0.8f, 0.8f),
            1.5f,
            new Color(0.75f, 0.2f, 0.45f)
        );
        SpawnBox(
            new Vector3(-1.2f, 1.7f, 0f),
            new Vector3(0.7f, 0.7f, 0.7f),
            1.2f,
            new Color(0.9f, 0.85f, 0.2f)
        );
        SpawnBox(new Vector3(4f, 0.5f, -3f), Vector3.one, 3f, new Color(0.4f, 0.8f, 0.35f));
        SpawnSphere(new Vector3(1.4f, 0.5f, 2f), 0.45f, 1.4f, new Color(0.95f, 0.55f, 0.2f));
        SpawnSphere(new Vector3(-4f, 0.5f, -2f), 0.6f, 2.2f, new Color(0.55f, 0.3f, 0.8f));

        spawned = true;
    }

    void SpawnBox(Vector3 position, Vector3 scale, float mass, Color color)
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        NetworkObject nob = nm.GetPooledInstantiated(
            physicsBoxPrefab,
            position,
            Quaternion.identity,
            true
        );
        nob.transform.localScale = scale;
        if (nob.TryGetComponent(out Rigidbody body))
            body.mass = mass;
        nm.ServerManager.Spawn(nob);
        if (nob.TryGetComponent(out NetworkPhysicsProp prop))
            prop.SetColor(color);
        else
            ArenaBuilder.ApplyColor(nob.gameObject, color);
    }

    void SpawnSphere(Vector3 position, float radius, float mass, Color color)
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        NetworkObject nob = nm.GetPooledInstantiated(
            physicsSpherePrefab,
            position,
            Quaternion.identity,
            true
        );
        nob.transform.localScale = Vector3.one * (radius * 2f);
        if (nob.TryGetComponent(out Rigidbody body))
            body.mass = mass;
        nm.ServerManager.Spawn(nob);
        if (nob.TryGetComponent(out NetworkPhysicsProp prop))
            prop.SetColor(color);
        else
            ArenaBuilder.ApplyColor(nob.gameObject, color);
    }
}
