#if UNITY_EDITOR
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class CoopPrototypeSetup
{
    const string PrefabFolder = "Assets/Prefabs";
    const string ScenesFolder = "Assets/Scenes";
    const string PlayerPrefabPath = PrefabFolder + "/NetworkPlayer.prefab";
    const string BoxPrefabPath = PrefabFolder + "/NetworkPhysicsBox.prefab";
    const string SpherePrefabPath = PrefabFolder + "/NetworkPhysicsSphere.prefab";
    const string MainMenuPath = ScenesFolder + "/MainMenu.unity";
    const string GameplayPath = ScenesFolder + "/Gameplay.unity";

    [MenuItem("Tools/Coop Prototype/Setup Prefabs And Scenes")]
    public static void SetupAll()
    {
        EnsureFolders();
        NetworkObject player = CreatePlayerPrefab();
        NetworkObject box = CreatePhysicsPrefab(PrimitiveType.Cube, "NetworkPhysicsBox", BoxPrefabPath, 2.5f);
        NetworkObject sphere = CreatePhysicsPrefab(PrimitiveType.Sphere, "NetworkPhysicsSphere", SpherePrefabPath, 1.5f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorApplication.ExecuteMenuItem("Tools/Fish-Networking/Utility/Refresh Default Prefabs");

        player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponent<NetworkObject>();
        box = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPrefabPath).GetComponent<NetworkObject>();
        sphere = AssetDatabase.LoadAssetAtPath<GameObject>(SpherePrefabPath).GetComponent<NetworkObject>();

        CreateMainMenuScene();
        CreateGameplayScene(player, box, sphere);
        UpdateBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "Coop Prototype",
            "Setup listo.\n\n1) Abre Scenes/MainMenu\n2) Pulsa Play\n3) Host en una instancia, Join 127.0.0.1 en otra (MPPM o build).",
            "OK");
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(ScenesFolder))
            AssetDatabase.CreateFolder("Assets", "Scenes");
    }

    static NetworkObject CreatePlayerPrefab()
    {
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "NetworkPlayer";
        Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());

        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.35f;
        controller.center = new Vector3(0f, 0.9f, 0f);

        var cameraPivot = new GameObject("CameraPivot");
        cameraPivot.transform.SetParent(player.transform);
        cameraPivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        var cameraObject = new GameObject("PlayerCamera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData));
        cameraObject.transform.SetParent(cameraPivot.transform);
        cameraObject.transform.localPosition = Vector3.zero;
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.enabled = false;
        camera.nearClipPlane = 0.08f;
        camera.fieldOfView = 75f;
        cameraObject.GetComponent<AudioListener>().enabled = false;

        PlayerInput input = player.AddComponent<PlayerInput>();
        input.actions = InputSystem.actions;
        input.defaultActionMap = "Player";
        input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
        input.camera = camera;
        input.enabled = false;

        player.AddComponent<NetworkObject>();
        NetworkTransform nt = player.AddComponent<NetworkTransform>();
        ConfigureNetworkTransform(nt, clientAuthoritative: true, NetworkTransform.ComponentConfigurationType.CharacterController);

        NetworkPlayerController movement = player.AddComponent<NetworkPlayerController>();
        NetworkPlayerGrab grab = player.AddComponent<NetworkPlayerGrab>();

        SerializedObject movementSo = new SerializedObject(movement);
        movementSo.FindProperty("cameraPivot").objectReferenceValue = cameraPivot.transform;
        movementSo.FindProperty("playerCamera").objectReferenceValue = camera;
        movementSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject grabSo = new SerializedObject(grab);
        grabSo.FindProperty("playerCamera").objectReferenceValue = camera;
        grabSo.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
        Object.DestroyImmediate(player);
        return prefab.GetComponent<NetworkObject>();
    }

    static NetworkObject CreatePhysicsPrefab(PrimitiveType type, string name, string path, float mass)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;

        Rigidbody body = go.AddComponent<Rigidbody>();
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;

        go.AddComponent<NetworkObject>();
        NetworkTransform nt = go.AddComponent<NetworkTransform>();
        ConfigureNetworkTransform(nt, clientAuthoritative: false, NetworkTransform.ComponentConfigurationType.Rigidbody);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab.GetComponent<NetworkObject>();
    }

    static void ConfigureNetworkTransform(NetworkTransform nt, bool clientAuthoritative, FishNet.Component.Transforming.NetworkTransform.ComponentConfigurationType config)
    {
        SerializedObject so = new SerializedObject(nt);
        so.FindProperty("_clientAuthoritative").boolValue = clientAuthoritative;
        so.FindProperty("_sendToOwner").boolValue = !clientAuthoritative;
        so.FindProperty("_componentConfiguration").enumValueIndex = (int)config;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void CreateMainMenuScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        Camera cam = Object.FindFirstObjectByType<Camera>();
        if (cam != null)
        {
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        var menu = new GameObject("MainMenu");
        menu.AddComponent<MainMenuController>();

        EditorSceneManager.SaveScene(scene, MainMenuPath);
    }

    static void CreateGameplayScene(NetworkObject playerPrefab, NetworkObject boxPrefab, NetworkObject spherePrefab)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // NetworkManager
        var nmGo = new GameObject("NetworkManager");
        NetworkManager nm = nmGo.AddComponent<NetworkManager>();
        Tugboat tugboat = nmGo.AddComponent<Tugboat>();
        SerializedObject tugSo = new SerializedObject(tugboat);
        SerializedProperty portProp = tugSo.FindProperty("_port");
        if (portProp != null)
            portProp.intValue = GameSessionConfig.DefaultPort;
        SerializedProperty reuseProp = tugSo.FindProperty("_reuseAddress");
        if (reuseProp != null)
            reuseProp.boolValue = true;
        tugSo.ApplyModifiedPropertiesWithoutUndo();

        DefaultPrefabObjects defaultPrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>("Assets/DefaultPrefabObjects.asset");
        if (defaultPrefabs != null)
        {
            SerializedObject nmSo = new SerializedObject(nm);
            SerializedProperty spawnable = nmSo.FindProperty("_spawnablePrefabs");
            if (spawnable != null)
                spawnable.objectReferenceValue = defaultPrefabs;
            nmSo.ApplyModifiedPropertiesWithoutUndo();
        }

        PlayerSpawner spawner = nmGo.AddComponent<PlayerSpawner>();
        spawner.SetPlayerPrefab(playerPrefab);

        // Spawn points
        var spawnsRoot = new GameObject("SpawnPoints");
        Transform[] spawns = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            var spawn = new GameObject($"Spawn_{i}");
            spawn.transform.SetParent(spawnsRoot.transform);
            spawn.transform.position = ArenaBuilder.SpawnPosition(i);
            spawn.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            spawns[i] = spawn.transform;
        }

        SerializedObject spawnerSo = new SerializedObject(spawner);
        SerializedProperty spawnsProp = spawnerSo.FindProperty("Spawns");
        spawnsProp.arraySize = spawns.Length;
        for (int i = 0; i < spawns.Length; i++)
            spawnsProp.GetArrayElementAtIndex(i).objectReferenceValue = spawns[i];
        spawnerSo.ApplyModifiedPropertiesWithoutUndo();

        var bootstrapGo = new GameObject("GameplayBootstrap");
        GameplayBootstrap bootstrap = bootstrapGo.AddComponent<GameplayBootstrap>();
        OnlineArenaSpawner arenaSpawner = bootstrapGo.AddComponent<OnlineArenaSpawner>();
        LocalSplitScreenSession localSession = bootstrapGo.AddComponent<LocalSplitScreenSession>();
        localSession.enabled = false;

        SerializedObject arenaSo = new SerializedObject(arenaSpawner);
        arenaSo.FindProperty("physicsBoxPrefab").objectReferenceValue = boxPrefab;
        arenaSo.FindProperty("physicsSpherePrefab").objectReferenceValue = spherePrefab;
        arenaSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject bootSo = new SerializedObject(bootstrap);
        bootSo.FindProperty("networkManager").objectReferenceValue = nm;
        bootSo.FindProperty("localSession").objectReferenceValue = localSession;
        bootSo.FindProperty("onlineArenaSpawner").objectReferenceValue = arenaSpawner;
        bootSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, GameplayPath);
    }

    static void UpdateBuildSettings()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MainMenuPath, true),
            new EditorBuildSettingsScene(GameplayPath, true)
        };
    }
}
#endif
