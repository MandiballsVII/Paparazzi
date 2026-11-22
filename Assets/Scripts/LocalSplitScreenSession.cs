using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Split-screen local usando el mismo prefab NetworkPlayer que online
/// (FishNet se elimina solo en la instancia).
/// </summary>
public class LocalSplitScreenSession : MonoBehaviour
{
    const int MaxPlayers = 4;
    const string NetworkPlayerPrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";

    [SerializeField]
    GameObject networkPlayerPrefab;

    readonly List<PlayerInput> players = new List<PlayerInput>();
    readonly HashSet<InputDevice> claimedDevices = new HashSet<InputDevice>();
    bool keyboardJoined;
    bool started;
    bool lobbyMode;

    public void SetLobbyMode(bool enabledLobby)
    {
        lobbyMode = enabledLobby;
    }

    /// <summary>Lobby: reaplica lock de cursor (el Update no corre en lobbyMode).</summary>
    public void RefreshCursorForLobby() => RefreshCursor();

    public void BeginSession()
    {
        if (started)
            return;

        started = true;
        lobbyMode = false;
        enabled = true;

        EnsureNetworkPlayerPrefab();

        var propsRoot = new GameObject("PhysicsProps");
        ArenaBuilder.BuildLocalPhysicsProps(propsRoot.transform);

        if (Keyboard.current != null && Mouse.current != null)
            TryJoinKeyboardMouse();

        foreach (Gamepad pad in Gamepad.all)
            TryJoinGamepad(pad);

        RefreshViewports();
        RefreshCursor();
    }

    void Update()
    {
        if (!started || lobbyMode)
            return;

        if (players.Count < MaxPlayers)
        {
            foreach (Gamepad pad in Gamepad.all)
            {
                if (claimedDevices.Contains(pad))
                    continue;

                if (pad.startButton.wasPressedThisFrame)
                    TryJoinGamepad(pad);
            }
        }

        RefreshCursor();
    }

    void TryJoinKeyboardMouse()
    {
        if (keyboardJoined || players.Count >= MaxPlayers)
            return;

        Join("Keyboard&Mouse", Keyboard.current, Mouse.current);
        keyboardJoined = true;
        claimedDevices.Add(Keyboard.current);
        claimedDevices.Add(Mouse.current);
    }

    void TryJoinGamepad(Gamepad pad)
    {
        if (pad == null || claimedDevices.Contains(pad) || players.Count >= MaxPlayers)
            return;

        Join("Gamepad", pad);
        claimedDevices.Add(pad);
    }

    void Join(string controlScheme, params InputDevice[] devices)
    {
        int index = players.Count;
        GameObject instance = InstantiateNetworkPlayerInactive();
        if (instance == null)
            return;

        instance.name = $"NetworkPlayer (Local {index + 1})";

        if (instance.TryGetComponent(out PlayerInput earlyInput))
            PlayerInputUtility.EnsurePrivateActionAsset(earlyInput);

        // Quitar FishNet antes de activar (evita error de NetworkObject.Awake).
        LobbyPlayerTransfer.PrepareOfflineClone(instance, keepControl: true);

        if (instance.TryGetComponent(out CharacterController controller))
        {
            PlayerBodyMetrics.ApplyToCharacterController(controller);
            controller.enabled = false;
        }

        instance.transform.position = ArenaBuilder.SpawnPosition(index);
        instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        if (controller != null)
            controller.enabled = true;

        instance.SetActive(true);

        if (!instance.TryGetComponent(out PlayerInput player))
        {
            Debug.LogError("[Local] El NetworkPlayer clone no tiene PlayerInput.");
            Object.Destroy(instance);
            return;
        }

        player.neverAutoSwitchControlSchemes = true;
        PlayerInputUtility.EnsurePrivateActionAsset(player);
        try
        {
            player.SwitchCurrentControlScheme(controlScheme, devices);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Local] No se pudo emparejar input: {e.Message}");
        }

        player.enabled = true;

        Rect viewport = LobbyPlayerTransfer.ViewportFor(index, index + 1);
        int color = index % ArenaBuilder.PlayerColors.Length;
        LobbyPlayerTransfer.EnsureIdentity(instance, index, color, viewport, controlScheme);
        ArenaBuilder.ApplyColor(instance, ArenaBuilder.PlayerColors[color]);

        if (TryGetPlayerCamera(instance.transform, out Camera cam))
        {
            player.camera = cam;
            cam.rect = viewport;
            cam.enabled = true;
            if (cam.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            {
                cameraData.renderType = CameraRenderType.Base;
                cameraData.renderPostProcessing = false;
            }

            // PrepareOfflineClone ya inicializa movement/grab/pause/health;
            // reaplicar cámara/viewport y alarm HUD del prefab.
            if (instance.TryGetComponent(out PlayerPhotoCapture capture))
            {
                capture.Initialize(cam);
                capture.RebindActionsForPrivateAsset();
            }

            if (instance.TryGetComponent(out PlayerPauseMenu pause))
            {
                pause.Initialize(cam);
                pause.RebindActionsForPrivateAsset();
            }

            if (instance.TryGetComponent(out PlayerHealthHud hud))
            {
                hud.SetLocalHudAllowed(true);
                hud.Initialize(cam);
            }

            if (instance.TryGetComponent(out PlayerAlarmHud alarmHud))
            {
                alarmHud.SetLocalHudAllowed(true);
                alarmHud.Initialize();
            }
        }

        players.Add(player);
        RefreshViewports();
        RefreshCursor();
    }

    public void SetNetworkPlayerPrefab(GameObject prefab)
    {
        if (prefab != null)
            networkPlayerPrefab = prefab;
    }

    void EnsureNetworkPlayerPrefab()
    {
        if (networkPlayerPrefab != null)
            return;

#if UNITY_EDITOR
        networkPlayerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(NetworkPlayerPrefabPath);
#endif
        if (networkPlayerPrefab == null)
            networkPlayerPrefab = Resources.Load<GameObject>("NetworkPlayer");

        if (networkPlayerPrefab == null)
        {
            Debug.LogError(
                "[Local] Asigna NetworkPlayer.prefab en LocalSplitScreenSession " +
                $"(Inspector) o en {NetworkPlayerPrefabPath}.");
        }
    }

    /// <summary>
    /// Instantiate bajo padre inactivo: Awake de FishNet no corre hasta SetActive(true).
    /// </summary>
    GameObject InstantiateNetworkPlayerInactive()
    {
        if (networkPlayerPrefab == null)
        {
            EnsureNetworkPlayerPrefab();
            if (networkPlayerPrefab == null)
                return null;
        }

        var hide = new GameObject("LocalInactiveSpawn");
        hide.SetActive(false);

        GameObject instance = Object.Instantiate(networkPlayerPrefab, hide.transform, false);
        instance.SetActive(false);
        instance.transform.SetParent(null);
        Object.Destroy(hide);
        return instance;
    }

    static bool TryGetPlayerCamera(Transform root, out Camera cam)
    {
        cam = null;
        Transform pivot = root.Find("CameraPivot");
        if (pivot == null)
            return false;
        cam = pivot.GetComponentInChildren<Camera>(true);
        return cam != null;
    }

    void RefreshViewports()
    {
        int count = players.Count;
        for (int i = 0; i < count; i++)
        {
            if (players[i] == null || !TryGetPlayerCamera(players[i].transform, out Camera camera))
                continue;

            Rect view = LobbyPlayerTransfer.ViewportFor(i, count);
            camera.rect = view;

            if (players[i].TryGetComponent(out PlayerSessionIdentity identity))
                identity.SetViewport(view);

            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = i == 0;
        }
    }

    void RefreshCursor()
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null
                && players[i].TryGetComponent(out PlayerPauseMenu menu)
                && menu.IsOpen)
                return;
        }

        bool mousePlayer = keyboardJoined;
        Cursor.lockState = mousePlayer ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !mousePlayer;
    }
}
