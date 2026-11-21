using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Arranque del Lobby: coloca jugadores + liquida economía + News.
/// Salir al menú: pausa → Leave (MatchSessionExit).
/// </summary>
public class LobbyBootstrap : MonoBehaviour
{
    const string LobbySceneName = "Lobby";
    const string NewsObjectName = "News";

    bool activated;

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
        if (activated)
            return;
        activated = true;

        DisableSceneOnlyCameras();
        EnsureNewsBoard();
        Vector3 center = FindLobbyPlaneCenter();

        if (IsOnlineNetworkSession())
        {
            PlaceOnlinePlayersInLobby(center);
            LockCursorForLocalOwners();
            StartCoroutine(SettleEconomyOnlineNextFrame());
        }
        else
        {
            LobbyPlayerTransfer.PlacePreservedPlayersInLobby(center);
            LobbyPlayerTransfer.EnableLobbyGameplayControls();
            SettleEconomyLocal();
        }
    }

    static void EnsureNewsBoard()
    {
        GameObject news = GameObject.Find(NewsObjectName);
        if (news == null)
        {
            Debug.LogWarning("[Lobby] No hay objeto 'News' en la escena.");
            return;
        }

        if (!news.TryGetComponent(out NewsBoardPresenter _))
            news.AddComponent<NewsBoardPresenter>();
    }

    static void SettleEconomyLocal()
    {
        RunEconomy.ResetPendingSettlement();
        RunEconomy.SettleFromAllLocalAlbums();
        NewsBoardPresenter.RefreshAll();
    }

    IEnumerator SettleEconomyOnlineNextFrame()
    {
        RunEconomy.ResetPendingSettlement();

        const int maxAttempts = 30;
        bool submitted = false;

        for (int attempt = 0; attempt < maxAttempts && !submitted; attempt++)
        {
            yield return null;

            NetworkPlayerController[] players =
                Object.FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);

            bool anyOwner = false;
            bool allReady = true;

            for (int i = 0; i < players.Length; i++)
            {
                NetworkPlayerController player = players[i];
                if (player == null)
                    continue;

                int shots = player.TryGetComponent(out PlayerPhotoAlbum album) && album != null
                    ? album.Count
                    : -1;

                Debug.Log(
                    $"[Lobby] Settle scan attempt={attempt} go={player.name} id={player.GetInstanceID()} " +
                    $"scene={player.gameObject.scene.name} owner={player.IsOwner} shots={shots} " +
                    $"albumId={(album != null ? album.GetInstanceID() : 0)}");

                if (!player.IsOwner)
                    continue;

                anyOwner = true;
                if (!player.TryGetComponent(out PhotoEconomyReporter reporter))
                {
                    Debug.LogWarning(
                        "[Lobby] Falta PhotoEconomyReporter en NetworkPlayer (debe existir antes del spawn).");
                    allReady = false;
                    continue;
                }

                if (!reporter.SubmitAlbumForSettlement())
                    allReady = false;
            }

            if (anyOwner && allReady)
                submitted = true;
        }

        if (!submitted)
            Debug.LogWarning("[Lobby] Timeout reportando álbum para economía online.");

        NewsBoardPresenter.RefreshAll();
    }

    static bool IsOnlineNetworkSession()
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null)
            return false;
        return nm.IsServerStarted || nm.IsClientStarted;
    }

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
}
