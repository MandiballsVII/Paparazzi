using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resultado de valoración. CurrentValue está preparado para daño/lente en el futuro
/// (modificadores sobre OriginalValue sin perder la base).
/// </summary>
public class PhotoEvaluation
{
    public float OriginalValue { get; private set; }
    public float CurrentValue { get; private set; }

    public bool HasCelebrityInFrame { get; set; }
    public bool HasCelebrityPerformingAction { get; set; }

    readonly Dictionary<PhotoFactorId, float> factorScores = new();

    public bool IsControversialActionShot => HasCelebrityInFrame && HasCelebrityPerformingAction;

    public IReadOnlyDictionary<PhotoFactorId, float> FactorScores => factorScores;

    public void SetFactorScore(PhotoFactorId id, float score)
    {
        factorScores[id] = score;
    }

    public float GetFactorScore(PhotoFactorId id)
    {
        return factorScores.TryGetValue(id, out float score) ? score : 0f;
    }

    public void FinalizeValue(float computedOriginal)
    {
        OriginalValue = Mathf.Max(0f, computedOriginal);
        CurrentValue = OriginalValue;
    }

    /// <summary>
    /// Punto de extensión: golpes, lente, etc. reducirán CurrentValue
    /// (p.ej. −25% por golpe hasta mín. 25% de OriginalValue).
    /// </summary>
    public void ApplyValueMultiplier(float multiplier, float minFractionOfOriginal = 0.25f)
    {
        float minValue = OriginalValue * Mathf.Clamp01(minFractionOfOriginal);
        CurrentValue = Mathf.Max(minValue, CurrentValue * Mathf.Max(0f, multiplier));
    }
}
