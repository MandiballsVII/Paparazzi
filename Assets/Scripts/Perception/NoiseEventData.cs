using UnityEngine;

public enum NoiseType : byte
{
    Generic = 0,
    Footstep = 1,
    Landing = 2,
    Impact = 3,
    Voice = 4
}

/// <summary>
/// Evento de ruido diegético. Misma señal para IA, seguridad y (más adelante) FMOD/voz.
/// </summary>
public readonly struct NoiseEventData
{
    public readonly Vector3 Position;
    public readonly float Loudness;
    public readonly NoiseType Type;
    public readonly Transform Instigator;
    public readonly float Time;

    public NoiseEventData(Vector3 position, float loudness, NoiseType type, Transform instigator)
    {
        Position = position;
        Loudness = loudness;
        Type = type;
        Instigator = instigator;
        Time = UnityEngine.Time.time;
    }
}
