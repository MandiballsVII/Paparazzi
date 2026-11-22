using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD de estrellas amarillas + TestAlarm. Vive en el NetworkPlayer (PlayerHud).
/// </summary>
public class PlayerAlarmHud : MonoBehaviour
{
    [SerializeField]
    Transform yellowStarsRoot;

    [SerializeField]
    string yellowStarsRootName = "YellowAlarmStars";

    [SerializeField]
    Button testAlarmButton;

    [SerializeField]
    string testAlarmButtonName = "TestAlarm";

    [Tooltip("Obsoleto en HUD: el test de alarma está en el menú de pausa.")]
    [SerializeField]
    bool showTestAlarmButton = false;

    Image[] starFills;
    bool buttonWired;
    bool localHudAllowed = true;

    public void Initialize()
    {
        EnsureBindings();
        ApplyTestButtonVisibility();
        RefreshFills(AlarmManager.Instance != null ? AlarmManager.Instance.AlarmValue : 0f);
    }

    public void SetLocalHudAllowed(bool allowed)
    {
        localHudAllowed = allowed;
        ApplyTestButtonVisibility();
    }

    void Awake()
    {
        EnsureBindings();
    }

    void OnEnable()
    {
        EnsureBindings();
        WireButton();
        SubscribeAlarm();
        RefreshFills(AlarmManager.Instance != null ? AlarmManager.Instance.AlarmValue : 0f);
        ApplyTestButtonVisibility();
    }

    void Start()
    {
        // Por si AlarmManager spawnea después que el player.
        SubscribeAlarm();
        RefreshFills(AlarmManager.Instance != null ? AlarmManager.Instance.AlarmValue : 0f);
    }

    void LateUpdate()
    {
        // Fill continuo aunque el SyncVar llegue a menos Hz (clientes) o el decay sea por frame.
        if (!localHudAllowed || AlarmManager.Instance == null)
            return;

        RefreshFills(AlarmManager.Instance.AlarmValue);
    }

    void OnDisable()
    {
        UnsubscribeAlarm();
        UnwireButton();
    }

    void SubscribeAlarm()
    {
        if (AlarmManager.Instance == null)
            return;
        AlarmManager.Instance.AlarmChanged -= OnAlarmChanged;
        AlarmManager.Instance.AlarmChanged += OnAlarmChanged;
    }

    void UnsubscribeAlarm()
    {
        if (AlarmManager.Instance == null)
            return;
        AlarmManager.Instance.AlarmChanged -= OnAlarmChanged;
    }

    void OnValidate()
    {
        ApplyTestButtonVisibility();
    }

    void OnAlarmChanged(float alarm, float floor)
    {
        RefreshFills(alarm);
    }

    void RefreshFills(float alarm)
    {
        EnsureBindings();
        if (starFills == null)
            return;

        for (int i = 0; i < starFills.Length; i++)
        {
            if (starFills[i] == null)
                continue;
            starFills[i].fillAmount = Mathf.Clamp01(alarm - i);
        }
    }

    void EnsureBindings()
    {
        if (yellowStarsRoot == null)
            yellowStarsRoot = FindDeepChild(transform, yellowStarsRootName);

        if (yellowStarsRoot != null && (starFills == null || starFills.Length == 0))
        {
            int count = yellowStarsRoot.childCount;
            starFills = new Image[count];
            for (int i = 0; i < count; i++)
            {
                Transform child = yellowStarsRoot.GetChild(i);
                if (child != null)
                    child.TryGetComponent(out starFills[i]);
            }
        }

        if (testAlarmButton == null)
        {
            Transform buttonT = FindDeepChild(transform, testAlarmButtonName);
            if (buttonT != null)
                buttonT.TryGetComponent(out testAlarmButton);
        }
    }

    void ApplyTestButtonVisibility()
    {
        if (testAlarmButton == null)
            return;

        testAlarmButton.gameObject.SetActive(showTestAlarmButton);
        testAlarmButton.interactable = localHudAllowed && showTestAlarmButton;
    }

    void WireButton()
    {
        if (buttonWired || testAlarmButton == null)
            return;

        testAlarmButton.onClick.AddListener(OnTestAlarmClicked);
        buttonWired = true;
    }

    void UnwireButton()
    {
        if (!buttonWired || testAlarmButton == null)
            return;

        testAlarmButton.onClick.RemoveListener(OnTestAlarmClicked);
        buttonWired = false;
    }

    void OnTestAlarmClicked()
    {
        if (!localHudAllowed)
            return;

        if (AlarmManager.Instance == null)
        {
            Debug.LogWarning("[Alarm] No hay AlarmManager en la ronda.");
            return;
        }

        AlarmManager.Instance.RequestTestRaise();
    }

    static Transform FindDeepChild(Transform root, string objectName)
    {
        if (root == null || string.IsNullOrEmpty(objectName))
            return null;

        if (root.name == objectName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }
}
