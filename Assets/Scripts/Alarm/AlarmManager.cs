using System;
using System.Collections;
using FishNet;
using UnityEngine;

/// <summary>
/// Alarma de la ronda actual (0–5). Colocar en la escena del nivel (no DDOL).
/// Online: mismo GO con NetworkObject + NetworkAlarmManager (prefab de escena o spawnable).
/// </summary>
public class AlarmManager : MonoBehaviour
{
    public static AlarmManager Instance { get; private set; }

    [SerializeField]
    float maxAlarm = 5f;

    [SerializeField]
    float decayAmount = 0.1f;

    [SerializeField]
    float decayIntervalSeconds = 5f;

    [Tooltip("Multiplicador de ganancia por Floor(alarm). Índice 0 = nivel 0, … 5 = nivel 5.")]
    [SerializeField]
    float[] gainMultipliersByLevel = { 1f, 1f, 1f, 1f, 1f, 1f };

    [SerializeField]
    float testRaiseAmount = 0.3f;

    float alarmValue;
    float floorValue;
    Coroutine decayRoutine;

    public float AlarmValue => alarmValue;
    public float FloorValue => floorValue;
    public float MaxAlarm => maxAlarm;
    public float TestRaiseAmount => testRaiseAmount;

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
        StopDecayLoop();
    }

    void OnEnable()
    {
        if (HasAuthority())
            StartDecayLoop();
    }

    void OnDisable()
    {
        StopDecayLoop();
    }

    public void NotifyNetworkAuthorityReady()
    {
        if (HasAuthority())
            StartDecayLoop();
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
            Debug.Log($"[Alarm] Client → ServerRpc +{baseAmount} (scale={scaleByLevel})");
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
        SetAlarmInternal(alarmValue + amount, raiseFloorFromValue: true);
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

        if (TryGetComponent(out NetworkAlarmManager network) && network.IsServerAuthority)
            network.PushSync(alarmValue, floorValue);
    }

    public void ResetForNewRound(float startingAlarm = 0f)
    {
        float start = Mathf.Clamp(startingAlarm, 0f, maxAlarm - 0.001f);
        alarmValue = start;
        floorValue = Mathf.Floor(start);
        AlarmChanged?.Invoke(alarmValue, floorValue);

        if (TryGetComponent(out NetworkAlarmManager network) && network.IsServerAuthority)
            network.PushSync(alarmValue, floorValue);

        if (HasAuthority())
            StartDecayLoop();
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

    void SetAlarmInternal(float nextAlarm, bool raiseFloorFromValue)
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

        if (TryGetComponent(out NetworkAlarmManager network) && network.IsServerAuthority)
            network.PushSync(alarmValue, floorValue);
    }

    void ApplyDecayTick()
    {
        if (ActiveLevel >= Mathf.FloorToInt(maxAlarm))
            return;

        if (alarmValue <= floorValue + 0.0001f)
            return;

        SetAlarmInternal(alarmValue - decayAmount, raiseFloorFromValue: false);
    }

    /// <summary>Offline o proceso FishNet.</summary>
    bool HasAuthority()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null || (!nm.IsServerStarted && !nm.IsClientStarted))
            return true;

        return nm.IsServerStarted;
    }

    void StartDecayLoop()
    {
        StopDecayLoop();
        decayRoutine = StartCoroutine(DecayLoop());
    }

    void StopDecayLoop()
    {
        if (decayRoutine == null)
            return;
        StopCoroutine(decayRoutine);
        decayRoutine = null;
    }

    IEnumerator DecayLoop()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.1f, decayIntervalSeconds));
        while (enabled)
        {
            yield return wait;
            if (!HasAuthority())
                yield break;
            ApplyDecayTick();
        }
    }
}
