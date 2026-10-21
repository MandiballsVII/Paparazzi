using FishNet;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MatchSessionExit
{
    const string MainMenuSceneName = "MainMenu";

    public static void LeaveMatch()
    {
        NetworkManager networkManager = InstanceFinder.NetworkManager;
        if (networkManager == null)
            networkManager = Object.FindAnyObjectByType<NetworkManager>();

        if (networkManager != null)
        {
            if (networkManager.ServerManager != null && networkManager.IsServerStarted)
                networkManager.ServerManager.StopConnection(true);

            if (networkManager.ClientManager != null && networkManager.IsClientStarted)
                networkManager.ClientManager.StopConnection();
        }

        GameSessionConfig.Clear();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(MainMenuSceneName);
    }

    public static void QuitGame()
    {
        GameSessionConfig.Clear();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
