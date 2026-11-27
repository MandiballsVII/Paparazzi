using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Arranque directo de GymLevel: Play en la escena sin pasar por MainMenu.
/// Clona NetworkPlayer inactivo, quita FishNet solo en la instancia, y usa control local.
/// </summary>
public class GymBootstrap : MonoBehaviour
{
    const string GymSceneName = "GymLevel";
    const string PlayerPrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";
    const int MaxPlayers = 4;

    [SerializeField]
    GameObject networkPlayerPrefab;

    [SerializeField]
    Transform spawnPointsRoot;

    readonly List<PlayerInput> players = new();
    readonly HashSet<InputDevice> claimedDevices = new();
    readonly List<Transform> spawnPoints = new();
    bool keyboardJoined;
    bool started;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreateIfNeeded()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != GymSceneName)
            return;

        if (Object.FindAnyObjectByType<GymBootstrap>() != null)
            return;

        var go = new GameObject("GymBootstrap");
        go.AddComponent<GymBootstrap>();
    }

    void Start()
    {
        if (started)
            return;

        started = true;
        GameSessionConfig.SetLocal();
        DisableSceneCameras();
        ResolvePrefab();
        CollectSpawnPoints();

        if (networkPlayerPrefab == null)
        {
            Debug.LogError(
                "[GymBootstrap] Falta NetworkPlayer.prefab. Asigna el prefab en el inspector."
            );
            return;
        }

        if (Keyboard.current != null && Mouse.current != null)
            TryJoinKeyboardMouse();

        foreach (Gamepad pad in Gamepad.all)
            TryJoinGamepad(pad);

        RefreshViewports();
        RefreshCursor();
    }

    void Update()
    {
        if (!started || players.Count >= MaxPlayers)
            return;

        foreach (Gamepad pad in Gamepad.all)
        {
            if (claimedDevices.Contains(pad))
                continue;

            if (pad.startButton.wasPressedThisFrame)
                TryJoinGamepad(pad);
        }

        RefreshCursor();
    }

    void ResolvePrefab()
    {
        if (networkPlayerPrefab != null)
            return;

#if UNITY_EDITOR
        networkPlayerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            PlayerPrefabPath
        );
#endif
    }

    void CollectSpawnPoints()
    {
        spawnPoints.Clear();

        if (spawnPointsRoot == null)
        {
            GameObject found = GameObject.Find("SpawnPoints");
            if (found != null)
                spawnPointsRoot = found.transform;
        }

        if (spawnPointsRoot != null)
        {
            for (int i = 0; i < spawnPointsRoot.childCount; i++)
            {
                Transform child = spawnPointsRoot.GetChild(i);
                if (child != null)
                    spawnPoints.Add(child);
            }

            spawnPoints.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        if (spawnPoints.Count == 0)
            Debug.LogWarning("[GymBootstrap] No hay SpawnPoints. Se usará origen + offset.");
    }

    void DisableSceneCameras()
    {
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null)
                continue;

            if (cam.GetComponentInParent<PlayerInput>() != null)
                continue;

            cam.enabled = false;
            if (cam.TryGetComponent(out AudioListener listener))
                listener.enabled = false;
        }
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

        instance.name = $"GymPlayer {index + 1}";

        // Quitar FishNet antes de activar. Scheme/map tras SetActive.
        LobbyPlayerTransfer.PrepareOfflineClone(
            instance,
            keepControl: true,
            controlScheme,
            devices
        );

        if (instance.TryGetComponent(out CharacterController controller))
        {
            PlayerBodyMetrics.ApplyToCharacterController(controller);
            controller.enabled = false;
        }

        Transform spawn = GetSpawn(index);
        if (spawn != null)
            instance.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        else
        {
            instance.transform.position = new Vector3(index * 2f, 0.5f, 0f);
            instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }

        if (controller != null)
            controller.enabled = true;

        instance.SetActive(true);

        if (!instance.TryGetComponent(out PlayerInput player))
        {
            Debug.LogError("[GymBootstrap] El clone no tiene PlayerInput.");
            Object.Destroy(instance);
            return;
        }

        LobbyPlayerTransfer.ActivatePlayerInput(player, controlScheme, devices);
        if (instance.TryGetComponent(out PlayerMovement movement))
            movement.RebindActions();

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

            if (movement != null)
                movement.Initialize(instance.transform.Find("CameraPivot"), cam);
            if (instance.TryGetComponent(out PlayerGrab grab))
            {
                grab.ClearNetworkHandlers();
                grab.GrabActive = true;
                grab.Initialize(cam);
            }
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
        }

        players.Add(player);
        RefreshViewports();
        RefreshCursor();
    }

    /// <summary>
    /// Instantiate bajo padre inactivo: Awake de FishNet no corre hasta SetActive(true).
    /// No modifica el prefab en disco.
    /// </summary>
    GameObject InstantiateNetworkPlayerInactive()
    {
        var hide = new GameObject("GymInactiveSpawn");
        hide.SetActive(false);

        GameObject instance = Object.Instantiate(networkPlayerPrefab, hide.transform, false);
        instance.SetActive(false);
        instance.transform.SetParent(null);
        Object.Destroy(hide);
        return instance;
    }

    Transform GetSpawn(int index)
    {
        if (spawnPoints.Count == 0)
            return null;
        return spawnPoints[Mathf.Clamp(index, 0, spawnPoints.Count - 1)];
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
            if (!TryGetPlayerCamera(players[i].transform, out Camera camera))
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
            if (players[i].TryGetComponent(out PlayerPauseMenu menu) && menu.IsOpen)
            {
                // Menú abierto: cursor libre para poder pulsar botones uGUI.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }
        }

        bool mousePlayer = keyboardJoined;
        Cursor.lockState = mousePlayer ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !mousePlayer;
    }

    void OnGUI()
    {
        if (!started)
            return;

        GUI.color = Color.white;
        GUI.Label(
            new Rect(16f, Screen.height - 50f, 900f, 40f),
            "GYM: Start mando = unir  |  NetworkPlayer offline (sin FishNet)  |  Spawn_0..N"
        );
    }
}
