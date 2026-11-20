using UnityEngine;

/// <summary>
/// Layers de entorno (TagManager): 6 = Floor, 7 = Walls.
/// </summary>
public static class EnvironmentLayers
{
    public const int Floor = 6;
    public const int Walls = 7;

    public static readonly LayerMask FloorMask = 1 << Floor;
    public static readonly LayerMask WallsMask = 1 << Walls;
    public static readonly LayerMask FloorAndWallsMask = FloorMask | WallsMask;

    public static bool IsFloor(int layer) => layer == Floor;

    public static bool IsWall(int layer) => layer == Walls;

    public static bool IsFloor(Component c) => c != null && IsFloor(c.gameObject.layer);

    public static bool IsWall(Component c) => c != null && IsWall(c.gameObject.layer);

    public static bool IsFloor(GameObject go) => go != null && IsFloor(go.layer);

    public static bool IsWall(GameObject go) => go != null && IsWall(go.layer);

    public static bool IsFloor(Collider col) => col != null && IsFloor(col.gameObject.layer);

    public static bool IsWall(Collider col) => col != null && IsWall(col.gameObject.layer);
}
