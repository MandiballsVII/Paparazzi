using System;
using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Alarma de la ronda actual (0–5). Colocar en la escena del nivel (no DDOL).
/// Online: mismo GO con NetworkObject + NetworkAlarmManager.
/// Decay continuo (unidades/segundo) para fill suave en HUD.
/// </summary>
public class AlarmManager : MonoBehaviour
{
    public static AlarmManager Instance { get; private set; }

    [SerializeField]
    float maxAlarm = 5f;

    [Tooltip("Unidades de alarma que bajan por segundo (1 = una estrella/segundo). Ej: 0.1 ≈ estrella cada 10 s.")]
    [SerializeField]
    float decayPerSecond = 0.1f;

    [Tooltip("Cada cuántos segundos se empuja el SyncVar en online (el HUD local ya es continuo).")]
    [SerializeField]
    float networkSyncInterval = 0.1f;

    [Tooltip("Multiplicador de ganancia por Floor(alarm). Índice 0 = nivel 0, … 5 = nivel 5.")]
    [SerializeField]
    float[] gainMultipliersByLevel = { 1f, 1f, 1f, 1f, 1f, 1f };

    [SerializeField]
    float testRaiseAmount = 0.3f;

    float alarmValue;
    float floorValue;
    float syncTimer;
    bool decayActive;

    public float AlarmValue => alarmValue;
    public float FloorValue => floorValue;
    public float MaxAlarm => maxAlarm;
    public float TestRaiseAmount => testRaiseAmount;
    public float DecayPerSecond => decayPerSecond;

    /// <summary>Nivel de comportamiento activo (estrella completa).</summary>
    public int ActiveLevel => Mathf.Clamp(Mathf.FloorToInt(alarmValue), 0, Mathf.FloorToInt(maxAlarm));

    public event Action<float, float> AlarmChanged;
    public event Action<int> AlarmLevelReached;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Alarm] Ya hay un AlarmManager; destruyendo duplicado.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        decayActive = false;
    }

    void OnEnable()
    {
        if (HasAuthority())
            decayActive = true;
    }

    void OnDisable()
    {
        decayActive = false;
    }

    void Update()
    {
        if (!decayActive || !HasAuthority())
            return;

        ApplyContinuousDecay(Time.deltaTime);
    }

    public void NotifyNetworkAuthorityReady()
    {
        decayActive = HasAuthority();
        syncTimer = 0f;
        PushNetworkSync(force: true);
    }

    /// <summary>
    /// Local split-screen: FishNet desactiva NetworkObjects de escena sin red.
    /// </summary>
    public static void ActivateAllForOfflinePlay()
    {
        AlarmManager[] alarms = UnityEngine.Object.FindObjectsByType<AlarmManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < alarms.Length; i++)
        {
            AlarmManager alarm = alarms[i];
            if (alarm == null)
                continue;

            if (alarm.TryGetComponent(out NetworkObject nob))
                nob.SetIsNetworked(false);

            alarm.gameObject.SetActive(true);
        }
    }

    public void RequestTestRaise()
    {
        RequestAdd(testRaiseAmount, scaleByLevel: false);
    }

    public void RequestAdd(float baseAmount, bool scaleByLevel = true)
    {
        if (baseAmount <= 0f)
            return;

        if (TryGetComponent(out NetworkAlarmManager network) && network.ShouldForwardToServer)
        {
            network.RequestAddServerRpc(baseAmount, scaleByLevel);
            return;
        }

        ApplyAdd(baseAmount, scaleByLevel);
    }

    public void ApplyAdd(float baseAmount, bool scaleByLevel)
    {
        if (!HasAuthority())
        {
            Debug.LogWarning(
                "[Alarm] ApplyAdd ignorado (sin autoridad server/offline). " +
                "¿AlarmManager de escena spawneado / NetworkObject listo?");
            return;
        }

        float amount = scaleByLevel ? baseAmount * GetGainMultiplier(ActiveLevel) : baseAmount;
        if (amount <= 0f)
            return;

        float before = alarmValue;
        SetAlarmInternal(alarmValue + amount, raiseFloorFromValue: true, pushNetwork: true);
        Debug.Log($"[Alarm] +{amount:0.##} → {before:0.##} → {alarmValue:0.##} (floor={floorValue:0.##})");
    }

    public void ApplyAuthoritativeState(float alarm, float floor, bool invokeLevelEvents)
    {
        float clampedAlarm = Mathf.Clamp(alarm, 0f, maxAlarm);
        float clampedFloor = Mathf.Clamp(floor, 0f, maxAlarm);
        if (clampedFloor > clampedAlarm)
            clampedAlarm = clampedFloor;

        int previousLevel = ActiveLevel;
        alarmValue = clampedAlarm;
        floorValue = clampedFloor;

        AlarmChanged?.Invoke(alarmValue, floorValue);

        if (invokeLevelEvents)
        {
            int newLevel = ActiveLevel;
            for (int level = previousLevel + 1; level <= newLevel; level++)
                AlarmLevelReached?.Invoke(level);
        }
    }

    public void ResetForNewRound(float startingAlarm = 0f)
    {
        float start = Mathf.Clamp(startingAlarm, 0f, maxAlarm - 0.001f);
        alarmValue = start;
        floorValue = Mathf.Floor(start);
        AlarmChanged?.Invoke(alarmValue, floorValue);
        PushNetworkSync(force: true);
        decayActive = HasAuthority();
        syncTimer = 0f;
    }

    public float GetStarFill(int starIndex)
    {
        if (starIndex < 0)
            return 0f;
        return Mathf.Clamp01(alarmValue - starIndex);
    }

    float GetGainMultiplier(int level)
    {
        if (gainMultipliersByLevel == null || gainMultipliersByLevel.Length == 0)
            return 1f;

        int index = Mathf.Clamp(level, 0, gainMultipliersByLevel.Length - 1);
        return Mathf.Max(0f, gainMultipliersByLevel[index]);
    }

    void ApplyContinuousDecay(float deltaTime)
    {
        if (decayPerSecond <= 0f || deltaTime <= 0f)
            return;

        if (ActiveLevel >= Mathf.FloorToInt(maxAlarm))
            return;

        if (alarmValue <= floorValue + 0.0001f)
            return;

        float next = alarmValue - decayPerSecond * deltaTime;
        SetAlarmInternal(next, raiseFloorFromValue: false, pushNetwork: false);

        syncTimer += deltaTime;
        float interval = Mathf.Max(0.05f, networkSyncInterval);
        if (syncTimer >= interval)
        {
            syncTimer = 0f;
            PushNetworkSync(force: false);
        }
    }

    void SetAlarmInternal(float nextAlarm, bool raiseFloorFromValue, bool pushNetwork)
    {
        float clamped = Mathf.Clamp(nextAlarm, floorValue, maxAlarm);
        int previousLevel = ActiveLevel;

        alarmValue = clamped;

        if (raiseFloorFromValue)
        {
            float newFloor = Mathf.Floor(alarmValue);
            if (newFloor > floorValue)
            {
                floorValue = newFloor;
                int reached = Mathf.FloorToInt(floorValue);
                for (int level = previousLevel + 1; level <= reached; level++)
                    AlarmLevelReached?.Invoke(level);
            }
        }

        AlarmChanged?.Invoke(alarmValue, floorValue);

        if (pushNetwork)
            PushNetworkSync(force: true);
    }

    void PushNetworkSync(bool force)
    {
        if (!TryGetComponent(out NetworkAlarmManager network) || !network.IsServerAuthority)
            return;

        network.PushSync(alarmValue, floorValue);
        if (force)
            syncTimer = 0f;
    }

    /// <summary>Offline o servidor FishNet.</summary>
    bool HasAuthority()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null || (!nm.IsServerStarted && !nm.IsClientStarted))
            return true;

        return nm.IsServerStarted;
    }
}
