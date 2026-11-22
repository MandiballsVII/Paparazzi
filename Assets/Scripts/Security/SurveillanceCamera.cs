using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Cámara de vigilancia: visión (Perception) + alarma. Estática o móvil (pan horizontal).
/// Jerarquía esperada: Mount (fijo) / HeadPivot (yaw) / Head (prisma) + Eye.
/// </summary>
public class SurveillanceCamera : NetworkBehaviour
{
    [SerializeField]
    bool isMobile;

    [SerializeField]
    float alarmRaiseAmount = 0.3f;

    [SerializeField]
    float detectionCooldownSeconds = 3f;

    [Tooltip("Arco total de barrido en yaw (grados). 75 → ±37.5 desde el reposo.")]
    [SerializeField]
    float yawSweepDegrees = 75f;

    [SerializeField]
    float panSpeedDegreesPerSecond = 20f;

    [Tooltip("Pitch fijo de la cabeza hacia abajo (grados).")]
    [SerializeField]
    float lookDownPitchDegrees = 35f;

    [SerializeField]
    Transform headPivot;

    [SerializeField]
    Transform head;

    [SerializeField]
    PerceptionAgent perception;

    [SerializeField]
    bool cameraEnabled = true;

    float nextRaiseAllowedTime;
    float panDirection = 1f;
    float restYaw;
    bool loggedMissingSpawn;

    public bool CameraEnabled
    {
        get => cameraEnabled;
        set
        {
            cameraEnabled = value;
            ApplyVisionEnabled();
        }
    }

    public bool IsMobile => isMobile;

    void Awake()
    {
        AutoWire();
        CacheRestYaw();
        ApplyLookDownPitch();
        ApplyVisionEnabled();
    }

    void OnValidate()
    {
        AutoWire();
        if (!Application.isPlaying)
        {
            CacheRestYaw();
            ApplyLookDownPitch();
        }
    }

    public static void ActivateAllForOfflinePlay()
    {
        SurveillanceCamera[] cameras = FindObjectsByType<SurveillanceCamera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < cameras.Length; i++)
        {
            SurveillanceCamera cam = cameras[i];
            if (cam == null)
                continue;

            if (cam.TryGetComponent(out NetworkObject nob))
                nob.SetIsNetworked(false);

            cam.gameObject.SetActive(true);
        }
    }

    void Update()
    {
        if (!cameraEnabled)
            return;

        if (!CanRunLogic())
            return;

        if (isMobile)
            TickPan(Time.deltaTime);

        TickDetection();
    }

    void TickPan(float deltaTime)
    {
        if (headPivot == null || deltaTime <= 0f)
            return;

        float half = Mathf.Max(0f, yawSweepDegrees) * 0.5f;
        Vector3 euler = headPivot.localEulerAngles;
        float yaw = NormalizeAngle(euler.y);
        yaw += panDirection * panSpeedDegreesPerSecond * deltaTime;

        float min = restYaw - half;
        float max = restYaw + half;
        if (yaw >= max)
        {
            yaw = max;
            panDirection = -1f;
        }
        else if (yaw <= min)
        {
            yaw = min;
            panDirection = 1f;
        }

        headPivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    void TickDetection()
    {
        if (perception == null)
            return;

        bool anyoneVisible = perception.CurrentlySeesTarget;
        if (!anyoneVisible)
            return;

        if (Time.time < nextRaiseAllowedTime)
            return;

        RaiseAlarm();
        nextRaiseAllowedTime = Time.time + Mathf.Max(0.05f, detectionCooldownSeconds);
    }

    void RaiseAlarm()
    {
        // TODO: reproducir sonido de detección de cámara (FMOD) cuando exista el evento.
        if (AlarmManager.Instance == null)
        {
            Debug.LogWarning("[SurveillanceCamera] No hay AlarmManager en la ronda.", this);
            return;
        }

        AlarmManager.Instance.RequestAdd(alarmRaiseAmount, scaleByLevel: true);
    }

    void AutoWire()
    {
        if (headPivot == null)
        {
            Transform found = transform.Find("HeadPivot");
            if (found != null)
                headPivot = found;
        }

        if (head == null && headPivot != null)
        {
            Transform found = headPivot.Find("Head");
            if (found != null)
                head = found;
        }

        if (perception == null)
        {
            if (head != null)
                head.TryGetComponent(out perception);
            if (perception == null)
                TryGetComponent(out perception);
        }
    }

    void CacheRestYaw()
    {
        if (headPivot == null)
            return;
        restYaw = NormalizeAngle(headPivot.localEulerAngles.y);
    }

    void ApplyLookDownPitch()
    {
        if (head == null)
            return;

        // Solo pitch; el yaw de barrido vive en headPivot.
        head.localRotation = Quaternion.Euler(lookDownPitchDegrees, 0f, 0f);
    }

    void ApplyVisionEnabled()
    {
        if (perception == null)
            return;

        // Sin oído en cámaras; visión según enabled.
        if (perception.TryGetComponent(out VisionSensor vision))
            vision.VisionEnabled = cameraEnabled;
    }

    bool CanRunLogic()
    {
        if (!IsNetworkSessionActive())
            return true;

        if (!IsSpawned)
        {
            if (!loggedMissingSpawn)
            {
                loggedMissingSpawn = true;
                Debug.LogWarning("[SurveillanceCamera] NetworkObject no spawneado; lógica en pausa.", this);
            }

            return false;
        }

        return IsServerInitialized;
    }

    static bool IsNetworkSessionActive()
    {
        var nm = InstanceFinder.NetworkManager;
        return nm != null && (nm.IsServerStarted || nm.IsClientStarted);
    }

    static float NormalizeAngle(float degrees)
    {
        float a = degrees;
        while (a > 180f)
            a -= 360f;
        while (a < -180f)
            a += 360f;
        return a;
    }
}
