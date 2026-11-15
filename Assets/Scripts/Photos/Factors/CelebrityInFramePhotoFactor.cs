using UnityEngine;

/// <summary>
/// El famoso es visible en cámara (viewport + line of sight).
/// Base escalable: más adelante se puede afinar centrado / cuerpo completo.
/// </summary>
public sealed class CelebrityInFramePhotoFactor : IPhotoFactor
{
    public PhotoFactorId Id => PhotoFactorId.CelebrityInFrame;

    public void Evaluate(in PhotoCaptureContext context, PhotoEvaluation evaluation)
    {
        evaluation.HasCelebrityInFrame = false;
        evaluation.SetFactorScore(Id, 0f);

        if (context.Camera == null || context.Celebrity == null)
            return;

        Transform subject = context.Celebrity.transform;
        Vector3 aim = subject.position + Vector3.up * 1.5f;
        Vector3 viewport = context.Camera.WorldToViewportPoint(aim);
        if (viewport.z <= 0f)
            return;
        if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
            return;

        float distance = Vector3.Distance(context.Camera.transform.position, aim);
        if (distance > context.MaxSubjectDistance)
            return;

        Vector3 origin = context.Camera.transform.position;
        Vector3 toAim = aim - origin;
        if (Physics.Raycast(origin, toAim.normalized, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform != subject && !hit.transform.IsChildOf(subject))
                return;
        }

        evaluation.HasCelebrityInFrame = true;
        evaluation.SetFactorScore(Id, 1f);
    }
}
