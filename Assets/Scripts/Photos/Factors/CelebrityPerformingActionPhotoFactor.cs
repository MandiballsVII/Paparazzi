/// <summary>
/// El famoso está en mitad de la acción polémica (PerformAction).
/// </summary>
public sealed class CelebrityPerformingActionPhotoFactor : IPhotoFactor
{
    public PhotoFactorId Id => PhotoFactorId.CelebrityPerformingAction;

    public void Evaluate(in PhotoCaptureContext context, PhotoEvaluation evaluation)
    {
        evaluation.HasCelebrityPerformingAction = false;
        evaluation.SetFactorScore(Id, 0f);

        if (context.Celebrity == null)
            return;

        if (context.Celebrity.CurrentState != CelebrityBrain.AiState.PerformAction)
            return;

        // Solo aporta si también está en cuadro (lo marca el otro factor).
        if (!evaluation.HasCelebrityInFrame)
            return;

        evaluation.HasCelebrityPerformingAction = true;
        evaluation.SetFactorScore(Id, 1f);
    }
}
