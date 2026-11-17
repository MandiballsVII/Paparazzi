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

        if (keepControl
            && source.TryGetComponent(out PlayerInput sourceInput)
            && clone.TryGetComponent(out PlayerInput cloneInput))
        {
            try
            {
                if (sourceInput.devices.Count > 0)
                {
                    var devices = new InputDevice[sourceInput.devices.Count];
                    for (int d = 0; d < sourceInput.devices.Count; d++)
                        devices[d] = sourceInput.devices[d];
                    cloneInput.SwitchCurrentControlScheme(sourceInput.currentControlScheme, devices);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[LobbyTransfer] No se pudo reasignar input: {e.Message}");
            }

            scheme = sourceInput.currentControlScheme ?? scheme;
            cloneInput.enabled = true;
        }
        else if (clone.TryGetComponent(out PlayerInput remoteInput))
        {
            remoteInput.enabled = false;
        }

        EnsureIdentity(clone, playerIndex, colorIndex, viewport, scheme);
        RebindLocalComponents(clone, keepControl);
        return clone;
    }

    /// <summary>
    /// Quita FishNet de un clone (Gym/Lobby). No modificar el prefab asset.
    /// </summary>
    public static void PrepareOfflineClone(GameObject go, bool keepControl = true)
    {
        StripNetworkComponents(go);
        RebindLocalComponents(go, keepControl);
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

    static void RebindLocalComponents(GameObject go, bool keepControl = true)
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
                PlayerInputUtility.EnsurePrivateActionAsset(input);
            input.enabled = keepControl;
        }

        if (keepControl)
        {
            if (!go.TryGetComponent(out PlayerMovement movement))
            {
                if (go.TryGetComponent(out CharacterController _) && pivot != null && cam != null)
                {
                    movement = go.AddComponent<PlayerMovement>();
                    movement.Initialize(pivot, cam);
                }
            }
            else if (pivot != null && cam != null)
            {
                movement.Initialize(pivot, cam);
            }

            if (!go.TryGetComponent(out PlayerGrab grab))
                grab = go.AddComponent<PlayerGrab>();
            if (grab != null && cam != null)
                grab.Initialize(cam);

            if (go.TryGetComponent(out PlayerPhotoCapture capture) && cam != null)
            {
                capture.Initialize(cam);
                capture.BindInput();
            }

            if (go.TryGetComponent(out PlayerPauseMenu pause) && cam != null)
            {
                pause.Initialize(cam);
                pause.RebindActionsForPrivateAsset();
            }

            if (go.TryGetComponent(out PlayerHealthHud hud) && cam != null)
                hud.Initialize(cam);

            if (go.TryGetComponent(out PlayerStance stance) && pivot != null)
                stance.Initialize(pivot);
        }
        else
        {
            // Avatar remoto: sin control, cámara off (el local ya tiene la suya).
            if (go.TryGetComponent(out PlayerMovement _))
            { /* leave component; Update will no-op sin input bound if disabled PI */ }
            if (cam != null)
                cam.enabled = false;
        }

        if (go.TryGetComponent(out CharacterController cc))
            cc.enabled = true;
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
