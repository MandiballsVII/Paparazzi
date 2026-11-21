using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orquesta factores de foto. Añadir nuevos IPhotoFactor aquí sin tocar el disparo.
/// </summary>
public static class PhotoEvaluator
{
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

        evaluation.FinalizeValue(GetPayoutValue(evaluation));
        return evaluation;
    }

    /// <summary>
    /// Polémica = 1000; famoso en frame (sin polémica) = 50; resto = 0.
    /// </summary>
    public static int GetPayoutValue(PhotoEvaluation evaluation)
    {
        if (evaluation == null)
            return 0;
        if (evaluation.IsControversialActionShot)
            return RunEconomyConstants.ControversialPhotoValue;
        if (evaluation.HasCelebrityInFrame)
            return RunEconomyConstants.FamousPhotoValue;
        return 0;
    }

    public static CelebrityBrain FindCelebrity()
    {
        return Object.FindAnyObjectByType<CelebrityBrain>();
    }
}
