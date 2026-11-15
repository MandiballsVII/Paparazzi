using UnityEngine;

/// <summary>
/// Datos del momento del disparo para evaluar la foto (extensible).
/// </summary>
public struct PhotoCaptureContext
{
    public Camera Camera;
    public Transform Photographer;
    public CelebrityBrain Celebrity;
    public float MaxSubjectDistance;
}
