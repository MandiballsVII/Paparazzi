using UnityEngine;

public static class ArenaBuilder
{
    public static readonly Color[] PlayerColors =
    {
        new Color(0.90f, 0.25f, 0.25f),
        new Color(0.25f, 0.45f, 0.95f),
        new Color(0.25f, 0.80f, 0.35f),
        new Color(0.95f, 0.85f, 0.20f)
    };

    public static Vector3 SpawnPosition(int index)
    {
        // CharacterController centrado (height 2) → transform en el centro; pies en y=0 con spawn.y=1.
        float x = (index - 1.5f) * 1.6f;
        return new Vector3(x, 1f, 6f);
    }

    public static GameObject BuildStaticArena()
    {
        var root = new GameObject("Arena");

        CreateStaticBox(root.transform, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(22f, 0.5f, 22f), new Color(0.35f, 0.35f, 0.38f));
        CreateStaticBox(root.transform, "WallNorth", new Vector3(0f, 2f, 11f), new Vector3(22f, 4f, 0.4f), new Color(0.55f, 0.55f, 0.58f));
        CreateStaticBox(root.transform, "WallSouth", new Vector3(0f, 2f, -11f), new Vector3(22f, 4f, 0.4f), new Color(0.55f, 0.55f, 0.58f));
        CreateStaticBox(root.transform, "WallEast", new Vector3(11f, 2f, 0f), new Vector3(0.4f, 4f, 22f), new Color(0.55f, 0.55f, 0.58f));
        CreateStaticBox(root.transform, "WallWest", new Vector3(-11f, 2f, 0f), new Vector3(0.4f, 4f, 22f), new Color(0.55f, 0.55f, 0.58f));

        return root;
    }

    public static void BuildLocalPhysicsProps(Transform parent)
    {
        CreateLocalPhysicsBox(parent, new Vector3(-2f, 0.6f, 0f), Vector3.one, 2.5f, new Color(0.85f, 0.45f, 0.15f));
        CreateLocalPhysicsBox(parent, new Vector3(0f, 0.6f, -1.5f), new Vector3(1.2f, 1.2f, 1.2f), 4f, new Color(0.2f, 0.6f, 0.8f));
        CreateLocalPhysicsBox(parent, new Vector3(2.2f, 0.4f, 0.4f), new Vector3(0.8f, 0.8f, 0.8f), 1.5f, new Color(0.75f, 0.2f, 0.45f));
        CreateLocalPhysicsBox(parent, new Vector3(-1.2f, 1.7f, 0f), new Vector3(0.7f, 0.7f, 0.7f), 1.2f, new Color(0.9f, 0.85f, 0.2f));
        CreateLocalPhysicsBox(parent, new Vector3(4f, 0.5f, -3f), Vector3.one, 3f, new Color(0.4f, 0.8f, 0.35f));
        CreateLocalPhysicsSphere(parent, new Vector3(1.4f, 0.5f, 2f), 0.45f, 1.4f, new Color(0.95f, 0.55f, 0.2f));
        CreateLocalPhysicsSphere(parent, new Vector3(-4f, 0.5f, -2f), 0.6f, 2.2f, new Color(0.55f, 0.3f, 0.8f));
    }

    public static Material CreateLitMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        var material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        else
            material.color = color;

        return material;
    }

    public static void ApplyColor(GameObject target, Color color)
    {
        if (!target.TryGetComponent(out MeshRenderer meshRenderer))
            return;

        meshRenderer.material = CreateLitMaterial(color);
    }

    static void CreateStaticBox(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent);
        box.transform.position = position;
        box.transform.localScale = scale;
        ApplyColor(box, color);
    }

    static void CreateLocalPhysicsBox(Transform parent, Vector3 position, Vector3 scale, float mass, Color color)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "PhysicsBox";
        box.transform.SetParent(parent);
        box.transform.position = position;
        box.transform.localScale = scale;
        ApplyColor(box, color);

        Rigidbody body = box.AddComponent<Rigidbody>();
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }

    static void CreateLocalPhysicsSphere(Transform parent, Vector3 position, float radius, float mass, Color color)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "PhysicsSphere";
        sphere.transform.SetParent(parent);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * (radius * 2f);
        ApplyColor(sphere, color);

        Rigidbody body = sphere.AddComponent<Rigidbody>();
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }
}
