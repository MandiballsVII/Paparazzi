using System;
using UnityEngine;

/// <summary>
/// Bus global de ruido. Emisores publican; HearingSensors se suscriben.
/// </summary>
public static class NoiseHub
{
    public static event Action<NoiseEventData> Emitted;

    public static void Emit(in NoiseEventData data)
    {
        if (data.Loudness <= 0f)
            return;

        Emitted?.Invoke(data);
    }

    public static void Emit(Vector3 position, float loudness, NoiseType type, Transform instigator = null)
    {
        Emit(new NoiseEventData(position, loudness, type, instigator));
    }
}
