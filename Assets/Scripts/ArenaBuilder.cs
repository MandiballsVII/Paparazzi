using UnityEngine;

public static class ArenaBuilder
{
    public static readonly Color[] PlayerColors =
    {
        new Color(0.90f, 0.25f, 0.25f),
        new Color(0.25f, 0.45f, 0.95f),
        new Color(0.25f, 0.80f, 0.35f),
        new Color(0.95f, 0.85f, 0.20f),
    };

    public static Vector3 SpawnPosition(int index)
    {
        // CharacterController centrado (height 1) → transform en el centro; pies en y=0 con spawn.y=0.5.
        float x = (index - 1.5f) * 1.6f;
        return new Vector3(x, 0.5f, 6f);
    }

    /// <summary>
    /// Posición recomendada en Gameplay: lejos de props (x±4,z±3) y spawns (z≈6),
    /// entrada (local -Z) mirando al centro de la arena. No pegado a paredes.
    /// </summary>
    public static readonly Vector3 EscapeVehicleScenePosition = new Vector3(0f, 0f, -12f);
    public static readonly Quaternion EscapeVehicleSceneRotation = Quaternion.Euler(0f, 180f, 0f);

    /// <summary>
    /// Placeholder de furgoneta/camión (raíz en origen local): caja abierta por detrás + rampa.
    /// Pensado para guardarse como prefab; la instancia en escena define posición/rotación.
    /// </summary>
    public static GameObject BuildEscapeVehicle(Transform parent = null)
    {
        var vehicle = new GameObject("EscapeVehicle");
        if (parent != null)
            vehicle.transform.SetParent(parent, false);
        vehicle.transform.localPosition = Vector3.zero;
        vehicle.transform.localRotation = Quaternion.identity;

        Color bodyColor = new Color(0.25f, 0.28f, 0.22f);
        Color rampColor = new Color(0.4f, 0.32f, 0.18f);
        Color cabinColor = new Color(0.2f, 0.22f, 0.35f);

        CreateStaticBoxLocal(
            vehicle.transform,
            "CargoFloor",
            new Vector3(0f, 0.55f, 0.5f),
            new Vector3(3.6f, 0.2f, 5.5f),
            bodyColor
        );
        CreateStaticBoxLocal(
            vehicle.transform,
            "WallLeft",
            new Vector3(-1.9f, 1.7f, 0.5f),
            new Vector3(0.15f, 2.2f, 5.5f),
            bodyColor
        );
        CreateStaticBoxLocal(
            vehicle.transform,
            "WallRight",
            new Vector3(1.9f, 1.7f, 0.5f),
            new Vector3(0.15f, 2.2f, 5.5f),
            bodyColor
        );
        CreateStaticBoxLocal(
            vehicle.transform,
            "WallFront",
            new Vector3(0f, 1.7f, 3.2f),
            new Vector3(3.9f, 2.2f, 0.15f),
            bodyColor
        );
        CreateStaticBoxLocal(
            vehicle.transform,
            "Roof",
            new Vector3(0f, 2.85f, 0.5f),
            new Vector3(3.9f, 0.12f, 5.5f),
            bodyColor
        );
        CreateStaticBoxLocal(
            vehicle.transform,
            "Cabin",
            new Vector3(0f, 1.5f, 4.4f),
            new Vector3(3.2f, 1.8f, 1.8f),
            cabinColor
        );

        // Rampa trasera (apertura en -Z local).
        var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = "Ramp";
        ramp.transform.SetParent(vehicle.transform, false);
        ramp.transform.localPosition = new Vector3(0f, 0.35f, -2.8f);
        ramp.transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
        ramp.transform.localScale = new Vector3(3.2f, 0.12f, 3.2f);
        ApplyColor(ramp, rampColor);

        var zone = new GameObject(
            "ReviveZone",
            typeof(BoxCollider),
            typeof(PlayerReviveZone),
            typeof(VehicleInteriorZone)
        );
        zone.transform.SetParent(vehicle.transform, false);
        zone.transform.localPosition = new Vector3(0f, 1.4f, 0.4f);
        BoxCollider trigger = zone.GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(3.2f, 2f, 4.5f);

        CreateExtractButton(vehicle.transform, zone.GetComponent<VehicleInteriorZone>());

        return vehicle;
    }

    public static void CreateExtractButton(Transform vehicleRoot, VehicleInteriorZone interiorZone)
    {
        if (vehicleRoot == null)
            return;

        Transform existing = vehicleRoot.Find("ExtractButton");
        if (existing != null)
        {
            if (!existing.TryGetComponent(out RoundExtractButton _))
                existing.gameObject.AddComponent<RoundExtractButton>();
            return;
        }

        // Panel interactuable en la cabina / pared frontal interior.
        GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
        button.name = "ExtractButton";
        button.transform.SetParent(vehicleRoot, false);
        button.transform.localPosition = new Vector3(0f, 1.55f, 2.85f);
        button.transform.localRotation = Quaternion.identity;
        button.transform.localScale = new Vector3(0.9f, 0.55f, 0.12f);
        ApplyColor(button, new Color(0.85f, 0.2f, 0.15f));

        RoundExtractButton extract = button.AddComponent<RoundExtractButton>();
        if (interiorZone != null)
            extract.SetInteriorZone(interiorZone);
    }

    /// <summary>Asegura zona + botón en un vehículo ya existente (prefab/escena).</summary>
    public static void EnsureEscapeVehicleExtractSetup(GameObject vehicle)
    {
        if (vehicle == null)
            return;

        Transform zoneT = vehicle.transform.Find("ReviveZone");
        VehicleInteriorZone interior;
        if (zoneT == null)
        {
            var zone = new GameObject(
                "ReviveZone",
                typeof(BoxCollider),
                typeof(PlayerReviveZone),
                typeof(VehicleInteriorZone)
            );
            zone.transform.SetParent(vehicle.transform, false);
            zone.transform.localPosition = new Vector3(0f, 1.4f, 0.4f);
            BoxCollider trigger = zone.GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(3.2f, 2f, 4.5f);
            interior = zone.GetComponent<VehicleInteriorZone>();
        }
        else
        {
            if (!zoneT.TryGetComponent(out PlayerReviveZone _))
                zoneT.gameObject.AddComponent<PlayerReviveZone>();
            if (!zoneT.TryGetComponent(out interior))
                interior = zoneT.gameObject.AddComponent<VehicleInteriorZone>();
        }

        CreateExtractButton(vehicle.transform, interior);
    }

    public static void BuildLocalPhysicsProps(Transform parent)
    {
        CreateLocalPhysicsBox(
            parent,
            new Vector3(-2f, 0.6f, 0f),
            Vector3.one,
            2.5f,
            new Color(0.85f, 0.45f, 0.15f)
        );
        CreateLocalPhysicsBox(
            parent,
            new Vector3(0f, 0.6f, -1.5f),
            new Vector3(1.2f, 1.2f, 1.2f),
            4f,
            new Color(0.2f, 0.6f, 0.8f)
        );
        CreateLocalPhysicsBox(
            parent,
            new Vector3(2.2f, 0.4f, 0.4f),
            new Vector3(0.8f, 0.8f, 0.8f),
            1.5f,
            new Color(0.75f, 0.2f, 0.45f)
        );
        CreateLocalPhysicsBox(
            parent,
            new Vector3(-1.2f, 1.7f, 0f),
            new Vector3(0.7f, 0.7f, 0.7f),
            1.2f,
            new Color(0.9f, 0.85f, 0.2f)
        );
        CreateLocalPhysicsBox(
            parent,
            new Vector3(4f, 0.5f, -3f),
            Vector3.one,
            3f,
            new Color(0.4f, 0.8f, 0.35f)
        );
        CreateLocalPhysicsSphere(
            parent,
            new Vector3(1.4f, 0.5f, 2f),
            0.45f,
            1.4f,
            new Color(0.95f, 0.55f, 0.2f)
        );
        CreateLocalPhysicsSphere(
            parent,
            new Vector3(-4f, 0.5f, -2f),
            0.6f,
            2.2f,
            new Color(0.55f, 0.3f, 0.8f)
        );
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
        MeshRenderer meshRenderer = null;
        Transform body = target.transform.Find("Body");
        if (body != null)
            body.TryGetComponent(out meshRenderer);

        if (meshRenderer == null)
            target.TryGetComponent(out meshRenderer);

        if (meshRenderer == null)
            return;

        meshRenderer.material = CreateLitMaterial(color);
    }

    /// <summary>Marcador de orientación a la altura de los pies (no tapa la cámara FPS).</summary>
    public static Vector3 FacingMarkerLocalPosition => new Vector3(0f, -0.45f, 0.42f);

    static void CreateStaticBoxLocal(
        Transform parent,
        string name,
        Vector3 localPosition,
        Vector3 scale,
        Color color
    )
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localRotation = Quaternion.identity;
        box.transform.localScale = scale;
        ApplyColor(box, color);

        if (name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
            box.layer = EnvironmentLayers.Walls;
        else if (name.IndexOf("Floor", System.StringComparison.OrdinalIgnoreCase) >= 0)
            box.layer = EnvironmentLayers.Floor;
    }

    static void CreateLocalPhysicsBox(
        Transform parent,
        Vector3 position,
        Vector3 scale,
        float mass,
        Color color
    )
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
        box.AddComponent<PropNavMeshObstacle>();
        box.AddComponent<ImpactNoiseEmitter>();
    }

    static void CreateLocalPhysicsSphere(
        Transform parent,
        Vector3 position,
        float radius,
        float mass,
        Color color
    )
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
        sphere.AddComponent<PropNavMeshObstacle>();
        sphere.AddComponent<ImpactNoiseEmitter>();
    }
}
