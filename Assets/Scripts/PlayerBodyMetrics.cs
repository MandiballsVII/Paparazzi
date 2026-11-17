using UnityEngine;

/// <summary>
/// Métricas canónicas del cuerpo del jugador (local + NetworkPlayer + Gym).
/// </summary>
public static class PlayerBodyMetrics
{
    public const float CapsuleHeight = 2f;
    public const float CapsuleRadius = 0.5f;
    public const float CapsuleSkinWidth = 0.08f;
    public static readonly Vector3 CapsuleCenter = Vector3.zero;
    public static readonly Vector3 CameraPivotLocalPosition = new Vector3(0f, 0.75f, 0f);

    public static void ApplyToCharacterController(CharacterController controller)
    {
        if (controller == null)
            return;

        controller.height = CapsuleHeight;
        controller.radius = CapsuleRadius;
        controller.center = CapsuleCenter;
        controller.skinWidth = CapsuleSkinWidth;
    }
}
