#if UNITY_EDITOR
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// NavMesh del Floor + prefab/instancia del famoso (cápsula grande + patrulla).
/// </summary>
public static class CelebritySetup
{
    const string PrefabFolder = "Assets/Prefabs";
    const string CelebrityPrefabPath = PrefabFolder + "/Celebrity.prefab";
    const string GameplayPath = "Assets/Scenes/Gameplay.unity";

    // Más grande que el player (cápsula ~altura 2, radio 0.5).
    const float CelebrityHeight = 3f;
    const float CelebrityRadius = 0.65f;

    /// <summary>Entry point batch: -executeMethod CelebritySetup.SetupAllBatch</summary>
    public static void SetupAllBatch()
    {
        SetupFloorNavMesh(showDialog: false);
        CreateCelebrityPrefabAndPlace(showDialog: false);
        Debug.Log("[CelebritySetup] NavMesh + Celebrity prefab/instancia listos.");
    }

    [MenuItem("Tools/Paparazzi/Add NavMesh Obstacles To Physics Prefabs")]
    public static void AddObstaclesToPhysicsPrefabs()
    {
        int updated = 0;
        updated += EnsurePropObstacleOnPrefab(PrefabFolder + "/NetworkPhysicsBox.prefab") ? 1 : 0;
        updated += EnsurePropObstacleOnPrefab(PrefabFolder + "/NetworkPhysicsSphere.prefab") ? 1 : 0;

        // Instancias ya colocadas en la escena abierta.
        Rigidbody[] bodies = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody rb = bodies[i];
            if (rb == null || rb.GetComponentInParent<CharacterController>() != null)
                continue;
            if (rb.GetComponentInParent<NavMeshAgent>() != null)
                continue;
            if (rb.TryGetComponent(out PropNavMeshObstacle _))
                continue;

            rb.gameObject.AddComponent<PropNavMeshObstacle>();
            updated++;
            EditorUtility.SetDirty(rb.gameObject);
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog(
            "Prop Obstacles",
            "PropNavMeshObstacle añadido a prefabs/instancias de props (" + updated + ").\n" +
            "El famoso evitará cajas/esferas y las empujará al chocar.",
            "OK");
    }

    static bool EnsurePropObstacleOnPrefab(string path)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
        if (prefabRoot == null)
            return false;

        bool changed = false;
        if (prefabRoot.GetComponent<Rigidbody>() != null && prefabRoot.GetComponent<PropNavMeshObstacle>() == null)
        {
            prefabRoot.AddComponent<PropNavMeshObstacle>();
            changed = true;
        }

        if (changed)
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);

        PrefabUtility.UnloadPrefabContents(prefabRoot);
        return changed;
    }

    [MenuItem("Tools/Paparazzi/Setup Floor NavMesh")]
    public static void SetupFloorNavMeshMenu() => SetupFloorNavMesh(showDialog: true);

    public static void SetupFloorNavMesh(bool showDialog = true)
    {
        Scene gameplay = EnsureGameplayOpen();
        GameObject floor = FindRootByName(gameplay, "Floor");
        if (floor == null)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("NavMesh", "No se encontró el GameObject 'Floor' en Gameplay.", "OK");
            else
                Debug.LogError("[CelebritySetup] No se encontró Floor en Gameplay.");
            return;
        }

        MarkNavigationStaticRecursive(floor);

        // Paredes / vehículo estáticos. No marcar rigidbodies dinámicos (cajas, esferas).
        foreach (GameObject root in gameplay.GetRootGameObjects())
        {
            if (root.name == "Floor")
                continue;
            if (root.GetComponentInChildren<Rigidbody>() != null)
                continue;
            if (root.GetComponentInChildren<MeshCollider>() == null
                && root.GetComponentInChildren<BoxCollider>() == null)
                continue;
            MarkNavigationStaticRecursive(root);
        }

        if (!floor.TryGetComponent(out NavMeshSurface surface))
            surface = floor.AddComponent<NavMeshSurface>();

        // All: el suelo genera walkable; paredes/vehículo restan huecos (no se atraviesan).
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();

        EditorSceneManager.MarkSceneDirty(gameplay);
        EditorSceneManager.SaveScene(gameplay);
        AssetDatabase.SaveAssets();

        Selection.activeGameObject = floor;
        if (showDialog)
        {
            EditorUtility.DisplayDialog(
                "NavMesh",
                "NavMeshSurface en Floor bakeado con CollectObjects = All (colliders).\n" +
                "Incluye paredes/props estáticos para que el famoso no los atraviese.\n" +
                "Si mueves geometría, vuelve a ejecutar este menú.",
                "OK");
        }
    }

    [MenuItem("Tools/Paparazzi/Create Celebrity Prefab And Place In Gameplay")]
    public static void CreateCelebrityPrefabAndPlaceMenu() => CreateCelebrityPrefabAndPlace(showDialog: true);

    public static void CreateCelebrityPrefabAndPlace(bool showDialog = true)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        SetupFloorNavMeshQuiet();

        GameObject built = BuildCelebrity();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(built, CelebrityPrefabPath);
        Object.DestroyImmediate(built);

        Scene gameplay = EnsureGameplayOpen();

        foreach (GameObject root in gameplay.GetRootGameObjects())
        {
            if (root.name == "Celebrity" || root.name.StartsWith("Celebrity"))
                Object.DestroyImmediate(root);
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "Celebrity";

        Vector3 spawn = Vector3.zero;
        if (NavMesh.SamplePosition(spawn, out NavMeshHit hit, 50f, NavMesh.AllAreas))
            spawn = hit.position;

        instance.transform.position = spawn;
        if (instance.TryGetComponent(out NavMeshAgent agent))
        {
            if (NavMesh.SamplePosition(spawn, out hit, 50f, NavMesh.AllAreas))
                agent.Warp(hit.position);
        }

        EditorSceneManager.MarkSceneDirty(gameplay);
        EditorSceneManager.SaveScene(gameplay);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = instance;
        if (showDialog)
        {
            EditorUtility.DisplayDialog(
                "Celebrity",
                "Prefab: Assets/Prefabs/Celebrity.prefab\n" +
                "Instancia en Gameplay.\n\n" +
                "Cápsula más grande que el player. Asigna el material en el MeshRenderer.\n" +
                "Patrulla aleatoria con NavMeshAgent (CelebrityPatrol).",
                "OK");
        }
    }

    static void SetupFloorNavMeshQuiet()
    {
        SetupFloorNavMesh(showDialog: false);
    }

    static void MarkNavigationStaticRecursive(GameObject root)
    {
        StaticEditorFlags flags =
            GameObjectUtility.GetStaticEditorFlags(root) | StaticEditorFlags.NavigationStatic;
        GameObjectUtility.SetStaticEditorFlags(root, flags);

        foreach (Transform child in root.transform)
            MarkNavigationStaticRecursive(child.gameObject);
    }

    static GameObject BuildCelebrity()
    {
        // Raíz en los pies (NavMeshAgent). Cuerpo visual centrado como hijo.
        var root = new GameObject("Celebrity");
        root.transform.position = Vector3.zero;

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);

        float scaleY = CelebrityHeight / 2f;
        float scaleXZ = CelebrityRadius / 0.5f;
        body.transform.localScale = new Vector3(scaleXZ, scaleY, scaleXZ);
        body.transform.localPosition = new Vector3(0f, CelebrityHeight * 0.5f, 0f);

        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.radius = CelebrityRadius * 0.9f;
        agent.height = CelebrityHeight;
        agent.speed = 2.2f;
        agent.angularSpeed = 100f;
        agent.acceleration = 6f;
        agent.stoppingDistance = 0.4f;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

        root.AddComponent<CelebrityPatrol>();
        return root;
    }

    static Scene EnsureGameplayOpen()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.path == GameplayPath)
            return active;

        return EditorSceneManager.OpenScene(GameplayPath, OpenSceneMode.Single);
    }

    static GameObject FindRootByName(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
                return root;
        }

        return null;
    }
}
#endif
