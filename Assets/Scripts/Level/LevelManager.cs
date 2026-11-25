using FishNet.Object;
using UnityEngine;

/// <summary>
/// Orquestador por nivel (en escena, no DDOL).
/// Referencias en Inspector: RoundSession, Alarm, Celebrity. Sin spawners vacíos en runtime.
/// </summary>
[DisallowMultipleComponent]
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("Round / Extract")]
    [SerializeField]
    NetworkRoundExtract roundSession;

    [Header("Alarm")]
    [SerializeField]
    AlarmManager alarmManager;

    [Header("Celebrity")]
    [SerializeField]
    CelebritySpawner celebritySpawner;

    [SerializeField]
    GameObject localCelebrityPrefab;

    [SerializeField]
    NetworkObject networkCelebrityPrefab;

    bool initialized;

    public NetworkRoundExtract RoundSession => roundSession;
    public AlarmManager Alarm => alarmManager;
    public CelebritySpawner Celebrity => celebritySpawner;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[LevelManager] Ya hay otro LevelManager en la escena.", this);
            return;
        }

        Instance = this;
        AutoWire();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnValidate()
    {
        AutoWire();
    }

    void AutoWire()
    {
        if (roundSession == null)
            roundSession = GetComponentInChildren<NetworkRoundExtract>(true);

        if (alarmManager == null)
            alarmManager = GetComponentInChildren<AlarmManager>(true);

        if (celebritySpawner == null
            && !TryGetComponent(out celebritySpawner))
            celebritySpawner = GetComponentInChildren<CelebritySpawner>(true);
    }

    /// <summary>Local split-screen: reactiva NetworkObjects de escena y spawnea celebrity offline.</summary>
    public void InitializeLocal()
    {
        if (initialized)
            return;
        if (!ValidateRefs())
            return;

        initialized = true;
        ApplyCelebrityPrefabs();
        ActivateNetworkedChildrenForOffline();
        SpawnCelebrity(networked: false);
        Debug.Log("[LevelManager] InitializeLocal OK", this);
    }

    /// <summary>
    /// Host: RoundSession/Alarm son NetworkObjects de escena (FishNet los gestiona).
    /// Celebrity se spawnea en red cuando el server esté listo.
    /// </summary>
    public void InitializeHost()
    {
        if (initialized)
            return;
        if (!ValidateRefs())
            return;

        initialized = true;
        ApplyCelebrityPrefabs();
        SpawnCelebrity(networked: true);
        Debug.Log("[LevelManager] InitializeHost OK", this);
    }

    /// <summary>Cliente: no spawnea; recibe RoundSession/Alarm/Celebrity por red.</summary>
    public void InitializeClient()
    {
        if (initialized)
            return;
        if (!ValidateRefs(requireCelebrityPrefabs: false))
            return;

        initialized = true;
        ApplyCelebrityPrefabs();
        Debug.Log("[LevelManager] InitializeClient OK (sin spawns locales)", this);
    }

    public void ActivateNetworkedChildrenForOffline()
    {
        if (roundSession != null)
        {
            if (roundSession.TryGetComponent(out NetworkObject roundNob))
                roundNob.SetIsNetworked(false);
            roundSession.gameObject.SetActive(true);
        }

        if (alarmManager != null)
        {
            if (alarmManager.TryGetComponent(out NetworkObject alarmNob))
                alarmNob.SetIsNetworked(false);
            alarmManager.gameObject.SetActive(true);
        }
    }

    bool ValidateRefs(bool requireCelebrityPrefabs = true)
    {
        bool ok = true;

        if (roundSession == null)
        {
            Debug.LogError(
                "[LevelManager] Falta Round Session (NetworkRoundExtract). " +
                "Asigna el hijo RoundSession en el prefab/escena.",
                this);
            ok = false;
        }

        if (alarmManager == null)
        {
            Debug.LogError(
                "[LevelManager] Falta Alarm Manager. " +
                "Puede ir en el mismo RoundSession o como hijo referenciado.",
                this);
            ok = false;
        }

        if (celebritySpawner == null)
        {
            Debug.LogError(
                "[LevelManager] Falta CelebritySpawner (componente en LevelManager o hijo).",
                this);
            ok = false;
        }

        if (requireCelebrityPrefabs)
        {
            if (localCelebrityPrefab == null)
            {
                Debug.LogError(
                    "[LevelManager] Asigna Local Celebrity Prefab (Assets/Prefabs/Celebrity.prefab).",
                    this);
                ok = false;
            }

            if (networkCelebrityPrefab == null
                && GameSessionConfig.Mode != GamePlayMode.LocalSplitScreen)
            {
                Debug.LogError(
                    "[LevelManager] Asigna Network Celebrity Prefab (Assets/Prefabs/NetworkCelebrity.prefab).",
                    this);
                ok = false;
            }
        }

        return ok;
    }

    void ApplyCelebrityPrefabs()
    {
        if (celebritySpawner == null)
            return;

        celebritySpawner.SetLocalPrefab(localCelebrityPrefab);
        celebritySpawner.SetNetworkPrefab(networkCelebrityPrefab);
    }

    void SpawnCelebrity(bool networked)
    {
        if (celebritySpawner == null)
            return;

        celebritySpawner.SpawnAtLevelStart(networked);
    }

#if UNITY_EDITOR
    public void EditorAssign(
        NetworkRoundExtract round,
        AlarmManager alarm,
        CelebritySpawner celebrity,
        GameObject localCeleb,
        NetworkObject networkCeleb)
    {
        roundSession = round;
        alarmManager = alarm;
        celebritySpawner = celebrity;
        localCelebrityPrefab = localCeleb;
        networkCelebrityPrefab = networkCeleb;
    }
#endif
}
