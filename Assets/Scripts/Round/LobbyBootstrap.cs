using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Arranque del Lobby: local (players DDOL) u online (NetworkPlayers en la misma sesión).
/// Salir al menú: pausa → Leave (MatchSessionExit).
/// </summary>
public class LobbyBootstrap : MonoBehaviour
{
    const string LobbySceneName = "Lobby";

    Text messageText;
    bool uiBuilt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, LobbySceneName, System.StringComparison.Ordinal))
            return;

        LobbyBootstrap existing = Object.FindAnyObjectByType<LobbyBootstrap>();
        if (existing != null)
        {
            existing.ActivateLobby();
            return;
        }

        var go = new GameObject("LobbyBootstrap");
        LobbyBootstrap bootstrap = go.AddComponent<LobbyBootstrap>();
        bootstrap.ActivateLobby();
    }

    void ActivateLobby()
    {
        DisableSceneOnlyCameras();
        Vector3 center = FindLobbyPlaneCenter();

        if (IsOnlineNetworkSession())
        {
            PlaceOnlinePlayersInLobby(center);
            LockCursorForLocalOwners();
        }
        else
        {
            LobbyPlayerTransfer.PlacePreservedPlayersInLobby(center);
            LobbyPlayerTransfer.EnableLobbyGameplayControls();
        }

        EnsureResultUi();
    }

    static bool IsOnlineNetworkSession()
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null)
            return false;
        return nm.IsServerStarted || nm.IsClientStarted;
    }

    /// <summary>
    /// Cada owner teleporta su NetworkPlayer (NT client-auth). Índices estables por ClientId.
    /// </summary>
    static void PlaceOnlinePlayersInLobby(Vector3 planeCenter)
    {
        NetworkPlayerController[] found =
            Object.FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);

        var owned = new List<NetworkPlayerController>(found.Length);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
                owned.Add(found[i]);
        }

        owned.Sort((a, b) =>
        {
            int idA = a.Owner.ClientId >= 0 ? a.Owner.ClientId : a.ObjectId;
            int idB = b.Owner.ClientId >= 0 ? b.Owner.ClientId : b.ObjectId;
            return idA.CompareTo(idB);
        });

        for (int i = 0; i < owned.Count; i++)
        {
            NetworkPlayerController player = owned[i];
            if (player == null || !player.IsOwner)
                continue;

            Vector3 pos = planeCenter + new Vector3((i - (owned.Count - 1) * 0.5f) * 2.2f, 1f, 0f);
            TeleportCharacter(player.gameObject, pos, Quaternion.Euler(0f, 180f, 0f));

            if (player.PlayerCamera != null)
            {
                player.PlayerCamera.enabled = true;
                player.PlayerCamera.rect = new Rect(0f, 0f, 1f, 1f);
                if (player.PlayerCamera.TryGetComponent(out AudioListener listener))
                    listener.enabled = true;
            }
        }
    }

    static void TeleportCharacter(GameObject go, Vector3 position, Quaternion rotation)
    {
        if (go.TryGetComponent(out CharacterController cc))
        {
            cc.enabled = false;
            go.transform.SetPositionAndRotation(position, rotation);
            cc.enabled = true;
            return;
        }

        go.transform.SetPositionAndRotation(position, rotation);
    }

    static void LockCursorForLocalOwners()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    static Vector3 FindLobbyPlaneCenter()
    {
        MeshFilter[] filters = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i] == null)
                continue;
            if (filters[i].sharedMesh != null && filters[i].sharedMesh.name.Contains("Plane"))
                return filters[i].transform.position;
            if (filters[i].name.IndexOf("Plane", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return filters[i].transform.position;
        }

        GameObject plane = GameObject.Find("Plane");
        if (plane != null)
            return plane.transform.position;

        return Vector3.zero;
    }

    static void DisableSceneOnlyCameras()
    {
        Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cams.Length; i++)
        {
            Camera cam = cams[i];
            if (cam == null)
                continue;
            if (cam.GetComponentInParent<PlayerSessionIdentity>() != null)
                continue;
            if (cam.GetComponentInParent<PlayerHealth>() != null)
                continue;
            if (cam.GetComponentInParent<NetworkPlayerController>() != null)
                continue;

            cam.enabled = false;
            if (cam.TryGetComponent(out AudioListener listener))
                listener.enabled = false;
        }
    }

    void EnsureResultUi()
    {
        if (uiBuilt)
            return;
        uiBuilt = true;

        var canvasGo = new GameObject("LobbyResultCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var panel = new GameObject("Banner", typeof(Image));
        panel.transform.SetParent(canvasGo.transform, false);
        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.15f, 0.72f);
        panelRt.anchorMax = new Vector2(0.85f, 0.95f);
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
        panel.GetComponent<Image>().raycastTarget = false;

        var textGo = new GameObject("ResultText", typeof(Text));
        textGo.transform.SetParent(panel.transform, false);
        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(16f, 8f);
        textRt.offsetMax = new Vector2(-16f, -8f);

        messageText = textGo.GetComponent<Text>();
        messageText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (messageText.font == null)
            messageText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        messageText.fontSize = 36;
        messageText.alignment = TextAnchor.MiddleCenter;
        messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
        messageText.verticalOverflow = VerticalWrapMode.Overflow;
        messageText.raycastTarget = false;

        if (RoundResultStore.HasResult)
        {
            messageText.text = RoundResultStore.Message;
            messageText.color = RoundResultStore.IsVictory
                ? new Color(0.45f, 0.95f, 0.55f)
                : new Color(0.95f, 0.75f, 0.35f);
        }
        else
        {
            messageText.text = "Lobby";
            messageText.color = Color.white;
        }
    }
}
