using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Preserva los mismos jugadores (color, input, viewports) al cambiar a Lobby.
/// Local: DontDestroyOnLoad de los players existentes.
/// Online: clona el jugador local sin componentes de red antes de parar FishNet.
/// </summary>
public static class LobbyPlayerTransfer
{
    static readonly List<GameObject> PreservedPlayers = new();
    static bool transferArmed;

    public static IReadOnlyList<GameObject> Players => PreservedPlayers;
    public static bool HasPreservedPlayers => PreservedPlayers.Count > 0;

    public static void PrepareBeforeLeaveGameplay()
    {
        ClearPreservedListOnly();
        transferArmed = true;

        if (GameSessionConfig.Mode == GamePlayMode.LocalSplitScreen)
            PreserveLocalSplitScreenPlayers();
        else
            PreserveOnlineLocalPlayers();

        // Sesión local: no debe seguir aceptando joins en Lobby.
        LocalSplitScreenSession session = Object.FindAnyObjectByType<LocalSplitScreenSession>();
        if (session != null)
        {
            session.SetLobbyMode(true);
            Object.DontDestroyOnLoad(session.gameObject);
        }
    }

    static void PreserveLocalSplitScreenPlayers()
    {
        PlayerSessionIdentity[] identities = Object.FindObjectsByType<PlayerSessionIdentity>(FindObjectsSortMode.None);
        System.Array.Sort(identities, (a, b) => a.PlayerIndex.CompareTo(b.PlayerIndex));

        for (int i = 0; i < identities.Length; i++)
        {
            if (identities[i] == null)
                continue;
            GameObject player = identities[i].gameObject;
            Object.DontDestroyOnLoad(player);
            PreservedPlayers.Add(player);
        }

        // Fallback si aún no tenían identity.
        if (PreservedPlayers.Count == 0)
        {
            PlayerHealth[] healths = Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
            for (int i = 0; i < healths.Length; i++)
            {
                if (healths[i] == null)
                    continue;
                EnsureIdentity(healths[i].gameObject, i, i, new Rect(0f, 0f, 1f, 1f), string.Empty);
                Object.DontDestroyOnLoad(healths[i].gameObject);
                PreservedPlayers.Add(healths[i].gameObject);
            }
        }
    }

    static void PreserveOnlineLocalPlayers()
    {
        // Clonar TODOS los players de red visibles en esta máquina (color + índice).
        // Solo el Owner conserva input; el resto es avatar en Lobby.
        NetworkPlayerController[] controllers = Object.FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            NetworkPlayerController net = controllers[i];
            if (net == null)
                continue;

            bool keepControl = net.IsOwner;
            GameObject lobbyPlayer = CloneAsOfflineLobbyPlayer(net.gameObject, i, keepControl);
            if (lobbyPlayer == null)
                continue;

            Object.DontDestroyOnLoad(lobbyPlayer);
            PreservedPlayers.Add(lobbyPlayer);
        }

        if (PreservedPlayers.Count == 0)
        {
            PlayerHealth[] healths = Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
            for (int i = 0; i < healths.Length; i++)
            {
                if (healths[i] == null)
                    continue;
                bool keepControl = !healths[i].TryGetComponent(out NetworkObject nob)
                                   || !nob.IsSpawned
                                   || nob.IsOwner;
                GameObject lobbyPlayer = CloneAsOfflineLobbyPlayer(healths[i].gameObject, i, keepControl);
                if (lobbyPlayer == null)
                    continue;
                Object.DontDestroyOnLoad(lobbyPlayer);
                PreservedPlayers.Add(lobbyPlayer);
            }
        }
    }

    static GameObject CloneAsOfflineLobbyPlayer(GameObject source, int fallbackIndex, bool keepControl)
    {
        if (source == null)
            return null;

        int colorIndex = fallbackIndex;
        Rect viewport = new Rect(0f, 0f, 1f, 1f);
        string scheme = string.Empty;
        int playerIndex = fallbackIndex;

        if (source.TryGetComponent(out PlayerSessionIdentity identity))
        {
            colorIndex = identity.ColorIndex;
            viewport = identity.Viewport;
            scheme = identity.ControlScheme;
            playerIndex = identity.PlayerIndex;
        }
        else if (source.TryGetComponent(out NetworkPlayerController net))
        {
            playerIndex = net.Owner.ClientId >= 0 ? net.Owner.ClientId : fallbackIndex;
            colorIndex = playerIndex % ArenaBuilder.PlayerColors.Length;
        }

        GameObject clone = Object.Instantiate(source);
        clone.name = source.name + "_Lobby";
        StripNetworkComponents(clone);

        InputDevice[] devices = null;
        if (keepControl && source.TryGetComponent(out PlayerInput sourceInput))
        {
            scheme = sourceInput.currentControlScheme ?? scheme;
            if (sourceInput.devices.Count > 0)
            {
                devices = new InputDevice[sourceInput.devices.Count];
                for (int d = 0; d < sourceInput.devices.Count; d++)
                    devices[d] = sourceInput.devices[d];
            }
        }

        EnsureIdentity(clone, playerIndex, colorIndex, viewport, scheme);
        // Online clone: Force asset + re-pair devices DESPUÉS (cambiar actions pierde el pairing).
        RebindLocalComponents(clone, keepControl, forceNewActionAsset: keepControl, scheme, devices);
        return clone;
    }

    /// <summary>
    /// Quita FishNet de un clone (Gym/Lobby). No modificar el prefab asset.
    /// Si el GO está inactivo, el ActionAsset se clona pero el scheme/map se aplica
    /// con <see cref="ActivatePlayerInput"/> tras SetActive(true).
    /// </summary>
    public static void PrepareOfflineClone(
        GameObject go,
        bool keepControl = true,
        string controlScheme = null,
        InputDevice[] devices = null)
    {
        StripNetworkComponents(go);
        RebindLocalComponents(
            go,
            keepControl,
            forceNewActionAsset: keepControl,
            controlScheme,
            devices);
    }

    /// <summary>
    /// Empareja scheme + mapa Player cuando el GO ya está activo (InputUser válido).
    /// </summary>
    public static void ActivatePlayerInput(
        PlayerInput input,
        string controlScheme = null,
        InputDevice[] devices = null)
    {
        if (input == null)
            return;

        input.neverAutoSwitchControlSchemes = true;
        ConfigureLobbyPlayerInput(
            input,
            forceNewActionAsset: false,
            controlScheme,
            devices);
    }

    public static void StripNetworkComponents(GameObject go)
    {
        // NetworkObserver depende de NetworkObject: quitarlo antes.
        FishNet.Observing.NetworkObserver[] observers =
            go.GetComponentsInChildren<FishNet.Observing.NetworkObserver>(true);
        for (int i = 0; i < observers.Length; i++)
        {
            if (observers[i] != null)
                Object.DestroyImmediate(observers[i]);
        }

        NetworkBehaviour[] behaviours = go.GetComponentsInChildren<NetworkBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null)
                Object.DestroyImmediate(behaviours[i]);
        }

        NetworkObject[] nobs = go.GetComponentsInChildren<NetworkObject>(true);
        for (int i = 0; i < nobs.Length; i++)
        {
            if (nobs[i] != null)
                Object.DestroyImmediate(nobs[i]);
        }
    }

    static void RebindLocalComponents(
        GameObject go,
        bool keepControl = true,
        bool forceNewActionAsset = false,
        string controlScheme = null,
        InputDevice[] devices = null)
    {
        // NetworkPlayer.prefab no trae pausa/foto/vida: los añadía NetworkPlayerController.Awake.
        // Tras strip FishNet ese Awake no corre → hay que crearlos aquí.
        EnsureOfflineGameplayComponents(go);

        Camera cam = go.GetComponentInChildren<Camera>(true);
        Transform pivot = go.transform.Find("CameraPivot");

        if (cam != null)
        {
            // En online, solo el player controlable usa cámara a pantalla completa;
            // PlacePreservedPlayersInLobby ajusta rects si hay varios.
            cam.enabled = keepControl;
            if (cam.TryGetComponent(out AudioListener listener))
                listener.enabled = keepControl;
        }

        if (go.TryGetComponent(out PlayerInput input))
        {
            if (keepControl)
                ConfigureLobbyPlayerInput(input, forceNewActionAsset, controlScheme, devices);
            else
                input.enabled = false;
        }

        if (keepControl)
        {
            EnsureLobbyPlayerMovement(go, pivot, cam);

            if (!go.TryGetComponent(out PlayerGrab grab))
                grab = go.AddComponent<PlayerGrab>();
            if (grab != null && cam != null)
                grab.Initialize(cam);

            if (go.TryGetComponent(out PlayerPhotoCapture capture) && cam != null)
            {
                capture.Initialize(cam);
                capture.RebindActionsForPrivateAsset();
            }

            if (go.TryGetComponent(out PlayerPauseMenu pause) && cam != null)
            {
                pause.Initialize(cam);
                pause.RebindActionsForPrivateAsset();
            }

            if (go.TryGetComponent(out PlayerHealthHud hud) && cam != null)
            {
                hud.SetLocalHudAllowed(true);
                hud.Initialize(cam);
            }

            if (go.TryGetComponent(out PlayerAlarmHud alarmHud))
            {
                alarmHud.SetLocalHudAllowed(true);
                alarmHud.Initialize();
            }

            if (go.TryGetComponent(out PlayerStance stance) && pivot != null)
                stance.Initialize(pivot);
        }
        else
        {
            // Avatar remoto: sin control, cámara off (el local ya tiene la suya).
            if (go.TryGetComponent(out PlayerMovement movement))
                movement.enabled = false;
            if (cam != null)
                cam.enabled = false;
        }

        if (go.TryGetComponent(out CharacterController cc))
            cc.enabled = true;
    }

    static void ConfigureLobbyPlayerInput(
        PlayerInput input,
        bool forceNewActionAsset,
        string controlScheme,
        InputDevice[] devices)
    {
        if (input == null)
            return;

        // Snapshot devices antes del swap (Force invalida el pairing).
        if (devices == null && input.devices.Count > 0)
        {
            devices = new InputDevice[input.devices.Count];
            for (int d = 0; d < input.devices.Count; d++)
                devices[d] = input.devices[d];
        }

        if (string.IsNullOrEmpty(controlScheme))
            controlScheme = input.currentControlScheme;

        input.enabled = false;

        if (forceNewActionAsset)
            PlayerInputUtility.ForcePrivateActionAsset(input);
        else
            PlayerInputUtility.EnsurePrivateActionAsset(input);

        input.defaultActionMap = "Player";

        // Sin GO activo no hay InputUser → SwitchControlScheme / ActionMap fallan.
        if (!input.gameObject.activeInHierarchy)
            return;

        input.enabled = true;
        TryApplyControlScheme(input, controlScheme, devices);

        try
        {
            input.SwitchCurrentActionMap("Player");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LobbyTransfer] SwitchCurrentActionMap Player: {e.Message}");
        }
    }

    static void TryApplyControlScheme(PlayerInput input, string controlScheme, InputDevice[] devices)
    {
        if (input == null)
            return;

        try
        {
            if (devices != null && devices.Length > 0)
            {
                string scheme = string.IsNullOrEmpty(controlScheme) ? "Keyboard&Mouse" : controlScheme;
                input.SwitchCurrentControlScheme(scheme, devices);
                return;
            }

            if (string.IsNullOrEmpty(controlScheme) || controlScheme == "Keyboard&Mouse")
            {
                if (Keyboard.current != null && Mouse.current != null)
                {
                    input.SwitchCurrentControlScheme("Keyboard&Mouse", Keyboard.current, Mouse.current);
                    return;
                }
            }

            if (controlScheme == "Gamepad" && Gamepad.current != null)
            {
                input.SwitchCurrentControlScheme("Gamepad", Gamepad.current);
                return;
            }

            if (Gamepad.current != null)
                input.SwitchCurrentControlScheme("Gamepad", Gamepad.current);
            else if (Keyboard.current != null && Mouse.current != null)
                input.SwitchCurrentControlScheme("Keyboard&Mouse", Keyboard.current, Mouse.current);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LobbyTransfer] ControlScheme: {e.Message}");
        }
    }

    static void EnsureLobbyPlayerMovement(GameObject go, Transform pivot, Camera cam)
    {
        if (pivot == null || cam == null || !go.TryGetComponent(out CharacterController _))
            return;

        if (!go.TryGetComponent(out PlayerMovement movement))
            movement = go.AddComponent<PlayerMovement>();

        movement.Initialize(pivot, cam);
        if (movement.enabled)
            movement.RebindActions();
        else
            movement.enabled = true; // OnEnable → BindActionsFromPlayerInput
    }

    /// <summary>
    /// Tras colocar en Lobby: mapa Player + cursor FPS.
    /// Local: no regenera InputActionAsset (rompe devices).
    /// Online: Force asset + re-pair devices.
    /// </summary>
    public static void EnableLobbyGameplayControls()
    {
        bool localSession = GameSessionConfig.Mode == GamePlayMode.LocalSplitScreen
                            || Object.FindAnyObjectByType<LocalSplitScreenSession>() != null;
        bool lockMouse = false;

        for (int i = 0; i < PreservedPlayers.Count; i++)
        {
            GameObject player = PreservedPlayers[i];
            if (player == null)
                continue;
            if (!player.TryGetComponent(out PlayerInput input))
                continue;

            // Avatares remotos online: input desactivado.
            if (!input.enabled && !localSession)
                continue;

            string scheme = input.currentControlScheme;
            if (string.IsNullOrEmpty(scheme) && player.TryGetComponent(out PlayerSessionIdentity id))
                scheme = id.ControlScheme;

            InputDevice[] devices = null;
            if (input.devices.Count > 0)
            {
                devices = new InputDevice[input.devices.Count];
                for (int d = 0; d < input.devices.Count; d++)
                    devices[d] = input.devices[d];
            }

            // Local DDOL: solo mapa Player. Online clone: Force + devices otra vez.
            ConfigureLobbyPlayerInput(input, forceNewActionAsset: !localSession, scheme, devices);

            Transform pivot = player.transform.Find("CameraPivot");
            Camera cam = null;
            if (player.TryGetComponent(out PlayerMovement existingMove))
                cam = existingMove.PlayerCamera;
            if (cam == null)
                cam = player.GetComponentInChildren<Camera>(true);

            EnsureLobbyPlayerMovement(player, pivot, cam);

            if (player.TryGetComponent(out PlayerPauseMenu pause) && cam != null)
            {
                pause.Initialize(cam);
                pause.RebindActionsForPrivateAsset();
            }

            scheme = input.currentControlScheme;
            if (string.IsNullOrEmpty(scheme) && player.TryGetComponent(out PlayerSessionIdentity id2))
                scheme = id2.ControlScheme;

            if (scheme == "Keyboard&Mouse" || string.IsNullOrEmpty(scheme))
                lockMouse = true;
        }

        LocalSplitScreenSession session = Object.FindAnyObjectByType<LocalSplitScreenSession>();
        if (session != null)
            session.RefreshCursorForLobby();

        if (lockMouse || (session != null && Keyboard.current != null))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    static void EnsureOfflineGameplayComponents(GameObject go)
    {
        if (!go.TryGetComponent(out PlayerStance _))
            go.AddComponent<PlayerStance>();

        if (!go.TryGetComponent(out PlayerHealth _))
            go.AddComponent<PlayerHealth>();

        if (!go.TryGetComponent(out PlayerRagdoll _))
            go.AddComponent<PlayerRagdoll>();

        if (!go.TryGetComponent(out PlayerHealthHud _))
            go.AddComponent<PlayerHealthHud>();

        // PlayerAlarmHud debe venir del prefab NetworkPlayer (YellowAlarmStars).
        if (!go.TryGetComponent(out PlayerAlarmHud _))
            Debug.LogWarning("[Lobby] Falta PlayerAlarmHud en el player (prefab NetworkPlayer).");

        if (!go.TryGetComponent(out PlayerPhotoAlbum _))
            go.AddComponent<PlayerPhotoAlbum>();

        if (!go.TryGetComponent(out PlayerPhotoCapture _))
            go.AddComponent<PlayerPhotoCapture>();

        if (!go.TryGetComponent(out PlayerPauseMenu _))
            go.AddComponent<PlayerPauseMenu>();

        if (!go.TryGetComponent(out PerceptionTarget _))
            go.AddComponent<PerceptionTarget>();

        if (!go.TryGetComponent(out FootstepNoiseEmitter _))
            go.AddComponent<FootstepNoiseEmitter>();
    }

    public static void EnsureIdentity(GameObject player, int index, int colorIndex, Rect viewport, string scheme)
    {
        if (!player.TryGetComponent(out PlayerSessionIdentity identity))
            identity = player.AddComponent<PlayerSessionIdentity>();
        identity.Configure(index, colorIndex, viewport, scheme);
    }

    public static void PlacePreservedPlayersInLobby(Vector3 planeCenter)
    {
        if (!transferArmed && PreservedPlayers.Count == 0)
            return;

        // Limpiar entradas destruidas.
        for (int i = PreservedPlayers.Count - 1; i >= 0; i--)
        {
            if (PreservedPlayers[i] == null)
                PreservedPlayers.RemoveAt(i);
        }

        PreservedPlayers.Sort((a, b) =>
        {
            int ia = a.TryGetComponent(out PlayerSessionIdentity idA) ? idA.PlayerIndex : 0;
            int ib = b.TryGetComponent(out PlayerSessionIdentity idB) ? idB.PlayerIndex : 0;
            return ia.CompareTo(ib);
        });

        int count = PreservedPlayers.Count;
        int controlledCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (PreservedPlayers[i] != null
                && PreservedPlayers[i].TryGetComponent(out PlayerInput pi)
                && pi.enabled)
                controlledCount++;
        }

        // Split-screen local: todos controlados. Online: normalmente 1 controlado a pantalla completa.
        bool splitAll = controlledCount > 1;

        int controlledOrdinal = 0;
        for (int i = 0; i < count; i++)
        {
            GameObject player = PreservedPlayers[i];
            if (player == null)
                continue;

            if (!player.TryGetComponent(out PlayerSessionIdentity identity))
                EnsureIdentity(player, i, i % ArenaBuilder.PlayerColors.Length, ViewportFor(i, count), string.Empty);

            identity.ApplyColor();

            CharacterController cc = null;
            player.TryGetComponent(out cc);
            if (cc != null)
                cc.enabled = false;

            Vector3 pos = planeCenter + new Vector3((i - (count - 1) * 0.5f) * 1.8f, 1f, 0f);
            player.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 180f, 0f));

            if (cc != null)
                cc.enabled = true;

            bool isControlled = player.TryGetComponent(out PlayerInput input) && input.enabled;
            Camera cam = null;
            if (player.TryGetComponent(out PlayerMovement movement))
                cam = movement.PlayerCamera;
            if (cam == null)
                cam = player.GetComponentInChildren<Camera>(true);

            if (cam != null && isControlled)
            {
                Rect view = splitAll
                    ? ViewportFor(controlledOrdinal, controlledCount)
                    : new Rect(0f, 0f, 1f, 1f);
                identity.SetViewport(view);
                cam.enabled = true;
                cam.rect = view;
                if (cam.TryGetComponent(out AudioListener listener))
                    listener.enabled = controlledOrdinal == 0;
                controlledOrdinal++;
            }
            else if (cam != null)
            {
                cam.enabled = false;
                if (cam.TryGetComponent(out AudioListener listener))
                    listener.enabled = false;
            }
        }

        transferArmed = false;
    }

    public static Rect ViewportFor(int index, int count)
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

    public static void DestroyPreservedPlayers()
    {
        LocalSplitScreenSession session = Object.FindAnyObjectByType<LocalSplitScreenSession>();
        if (session != null)
            Object.Destroy(session.gameObject);

        for (int i = 0; i < PreservedPlayers.Count; i++)
        {
            if (PreservedPlayers[i] != null)
                Object.Destroy(PreservedPlayers[i]);
        }

        ClearPreservedListOnly();
        transferArmed = false;
    }

    static void ClearPreservedListOnly()
    {
        PreservedPlayers.Clear();
    }
}
