using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orquesta factores de foto. Añadir nuevos IPhotoFactor aquí sin tocar el disparo.
/// </summary>
public static class PhotoEvaluator
{
    const float BaseScore = 10f;
    const float CelebrityInFrameBonus = 40f;
    const float ControversialActionBonus = 100f;

    static readonly List<IPhotoFactor> Factors = new()
    {
        new CelebrityInFramePhotoFactor(),
        new CelebrityPerformingActionPhotoFactor()
    };

    public static void RegisterFactor(IPhotoFactor factor)
    {
        if (factor == null)
            return;
        for (int i = 0; i < Factors.Count; i++)
        {
            if (Factors[i].Id == factor.Id)
            {
                Factors[i] = factor;
                return;
            }
        }

        Factors.Add(factor);
    }

    public static PhotoEvaluation Evaluate(in PhotoCaptureContext context)
    {
        var evaluation = new PhotoEvaluation();
        for (int i = 0; i < Factors.Count; i++)
            Factors[i].Evaluate(context, evaluation);

        float value = BaseScore;
        value += evaluation.GetFactorScore(PhotoFactorId.CelebrityInFrame) * CelebrityInFrameBonus;
        value += evaluation.GetFactorScore(PhotoFactorId.CelebrityPerformingAction) * ControversialActionBonus;
        evaluation.FinalizeValue(value);
        return evaluation;
    }

    public static CelebrityBrain FindCelebrity()
    {
        return Object.FindAnyObjectByType<CelebrityBrain>();
    }
}
