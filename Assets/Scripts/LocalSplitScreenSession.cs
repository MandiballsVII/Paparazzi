using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

public class LocalSplitScreenSession : MonoBehaviour
{
    const int MaxPlayers = 4;

    readonly List<PlayerInput> players = new List<PlayerInput>();
    readonly HashSet<InputDevice> claimedDevices = new HashSet<InputDevice>();
    GameObject playerTemplate;
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

        // El escenario lo aporta la escena Gameplay; solo spawneamos props agarrables.
        var propsRoot = new GameObject("PhysicsProps");
        ArenaBuilder.BuildLocalPhysicsProps(propsRoot.transform);
        playerTemplate = CreatePlayerTemplate();

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
        PlayerInput player = PlayerInput.Instantiate(
            playerTemplate,
            playerIndex: players.Count,
            controlScheme: controlScheme,
            pairWithDevices: devices);

        player.neverAutoSwitchControlSchemes = true;
        player.gameObject.name = $"Player {players.Count + 1}";
        PlayerInputUtility.EnsurePrivateActionAsset(player);

        Transform pivot = player.transform.Find("CameraPivot");
        Camera playerCamera = pivot.GetComponentInChildren<Camera>();
        player.camera = playerCamera;
        player.GetComponent<PlayerMovement>().Initialize(pivot, playerCamera);
        player.GetComponent<PlayerGrab>().Initialize(playerCamera);
        PlayerPhotoCapture photoCapture = player.GetComponent<PlayerPhotoCapture>();
        photoCapture.Initialize(playerCamera);
        photoCapture.RebindActionsForPrivateAsset();
        PlayerPauseMenu pauseMenu = player.GetComponent<PlayerPauseMenu>();
        pauseMenu.Initialize(playerCamera);
        pauseMenu.RebindActionsForPrivateAsset();
        player.GetComponent<PlayerHealthHud>().Initialize(playerCamera);

        player.gameObject.SetActive(true);

        CharacterController controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.position = ArenaBuilder.SpawnPosition(players.Count);
        player.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        controller.enabled = true;

        int index = players.Count;
        Rect view = LobbyPlayerTransfer.ViewportFor(index, index + 1);
        LobbyPlayerTransfer.EnsureIdentity(
            player.gameObject,
            index,
            index % ArenaBuilder.PlayerColors.Length,
            view,
            controlScheme);
        players.Add(player);
        RefreshViewports();
        RefreshCursor();
    }

    void RefreshViewports()
    {
        int count = players.Count;
        for (int i = 0; i < count; i++)
        {
            Camera camera = players[i].GetComponent<PlayerMovement>().PlayerCamera;
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
                return;
        }

        bool mousePlayer = keyboardJoined;
        Cursor.lockState = mousePlayer ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !mousePlayer;
    }

    GameObject CreatePlayerTemplate()
    {
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "PlayerTemplate";
        player.SetActive(false);
        Destroy(player.GetComponent<CapsuleCollider>());

        CharacterController controller = player.AddComponent<CharacterController>();
        PlayerBodyMetrics.ApplyToCharacterController(controller);

        var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
        face.name = "Face";
        Destroy(face.GetComponent<Collider>());
        face.transform.SetParent(player.transform, false);
        face.transform.localPosition = ArenaBuilder.FacingMarkerLocalPosition;
        face.transform.localScale = new Vector3(0.35f, 0.22f, 0.12f);
        ArenaBuilder.ApplyColor(face, new Color(0.08f, 0.08f, 0.1f));

        var cameraPivot = new GameObject("CameraPivot").transform;
        cameraPivot.SetParent(player.transform);
        cameraPivot.localPosition = PlayerBodyMetrics.CameraPivotLocalPosition;

        var cameraObject = new GameObject("PlayerCamera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData));
        cameraObject.transform.SetParent(cameraPivot);
        cameraObject.transform.localPosition = Vector3.zero;
        cameraObject.transform.localRotation = Quaternion.identity;

        Camera camera = cameraObject.GetComponent<Camera>();
        camera.nearClipPlane = 0.08f;
        camera.fieldOfView = 75f;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderType = CameraRenderType.Base;
        cameraData.renderPostProcessing = false;

        PlayerInput input = player.AddComponent<PlayerInput>();
        if (InputSystem.actions == null)
            Debug.LogError("No hay Input Actions asignadas en Project Settings > Input System Package.");
        input.actions = InputSystem.actions;
        PlayerInputUtility.EnsurePrivateActionAsset(input);
        input.defaultActionMap = "Player";
        input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
        input.camera = camera;
        input.neverAutoSwitchControlSchemes = true;

        player.AddComponent<PlayerStance>();
        player.AddComponent<PlayerHealth>();
        player.AddComponent<PlayerRagdoll>();
        player.AddComponent<PerceptionTarget>();
        player.AddComponent<FootstepNoiseEmitter>();

        PlayerMovement movement = player.AddComponent<PlayerMovement>();
        movement.Initialize(cameraPivot, camera);

        PlayerGrab grab = player.AddComponent<PlayerGrab>();
        grab.Initialize(camera);

        player.AddComponent<PlayerPhotoAlbum>();
        PlayerPhotoCapture capture = player.AddComponent<PlayerPhotoCapture>();
        capture.Initialize(camera);
        PlayerPauseMenu pauseMenu = player.AddComponent<PlayerPauseMenu>();
        pauseMenu.Initialize(camera);

        PlayerHealthHud healthHud = player.AddComponent<PlayerHealthHud>();
        healthHud.Initialize(camera);

        return player;
    }

    void OnGUI()
    {
        if (!started)
            return;

        GUI.color = Color.white;
        GUI.Label(new Rect(16f, Screen.height - 70f, 900f, 60f),
            "Local: Start mando = unir  |  E = coger (también cadáveres)  |  C = agachar  |  Foto / Menú\n" +
            "HUD vida arriba-izq de cada viewport. Botón daño test. Lleva muertos al vehículo (z+) para revivir.");
    }
}
