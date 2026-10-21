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

    public void BeginSession()
    {
        if (started)
            return;

        started = true;
        enabled = true;

        GameObject arena = ArenaBuilder.BuildStaticArena();
        ArenaBuilder.BuildLocalPhysicsProps(arena.transform);
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
        if (!started)
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

        Transform pivot = player.transform.Find("CameraPivot");
        Camera playerCamera = pivot.GetComponentInChildren<Camera>();
        player.camera = playerCamera;
        player.GetComponent<PlayerMovement>().Initialize(pivot, playerCamera);
        player.GetComponent<PlayerGrab>().Initialize(playerCamera);
        player.GetComponent<PlayerPhotoCapture>().Initialize(playerCamera);
        player.GetComponent<PlayerPauseMenu>().Initialize(playerCamera);

        player.gameObject.SetActive(true);

        CharacterController controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.position = ArenaBuilder.SpawnPosition(players.Count);
        player.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        controller.enabled = true;

        ArenaBuilder.ApplyColor(player.gameObject, ArenaBuilder.PlayerColors[players.Count]);
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
            camera.rect = ViewportFor(i, count);

            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = i == 0;
        }
    }

    static Rect ViewportFor(int index, int count)
    {
        switch (count)
        {
            case 1:
                return new Rect(0f, 0f, 1f, 1f);
            case 2:
                return index == 0
                    ? new Rect(0f, 0f, 0.5f, 1f)
                    : new Rect(0.5f, 0f, 0.5f, 1f);
            default:
                float x = (index % 2) * 0.5f;
                float y = index < 2 ? 0.5f : 0f;
                return new Rect(x, y, 0.5f, 0.5f);
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
        // Coincide con el mesh Capsule por defecto (alto 2, radio 0.5, centro 0).
        controller.height = 2f;
        controller.radius = 0.5f;
        controller.center = Vector3.zero;

        var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
        face.name = "Face";
        Destroy(face.GetComponent<Collider>());
        face.transform.SetParent(player.transform, false);
        face.transform.localPosition = ArenaBuilder.FacingMarkerLocalPosition;
        face.transform.localScale = new Vector3(0.35f, 0.22f, 0.12f);
        ArenaBuilder.ApplyColor(face, new Color(0.08f, 0.08f, 0.1f));

        var cameraPivot = new GameObject("CameraPivot").transform;
        cameraPivot.SetParent(player.transform);
        cameraPivot.localPosition = new Vector3(0f, 0.75f, 0f);

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
        input.defaultActionMap = "Player";
        input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
        input.camera = camera;
        input.neverAutoSwitchControlSchemes = true;

        player.AddComponent<PlayerStance>();

        PlayerMovement movement = player.AddComponent<PlayerMovement>();
        movement.Initialize(cameraPivot, camera);

        PlayerGrab grab = player.AddComponent<PlayerGrab>();
        grab.Initialize(camera);

        player.AddComponent<PlayerPhotoAlbum>();
        PlayerPhotoCapture capture = player.AddComponent<PlayerPhotoCapture>();
        capture.Initialize(camera);
        PlayerPauseMenu pauseMenu = player.AddComponent<PlayerPauseMenu>();
        pauseMenu.Initialize(camera);

        return player;
    }

    void OnGUI()
    {
        if (!started)
            return;

        GUI.color = Color.white;
        GUI.Label(new Rect(16f, 12f, 780f, 100f),
            "Local split-screen: teclado+ratón entra solo. Start en un mando libre para unir jugadores (máx. 4).\n" +
            "WASD / palanca izq: mover  |  Ratón / palanca der: mirar  |  Espacio / A: saltar\n" +
            "E / Y: coger/soltar  |  C: agachar  |  Click izq / RB: foto  |  Esc / Start: menú");
    }
}
