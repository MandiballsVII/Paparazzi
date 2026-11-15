using UnityEngine;

/// <summary>
/// Foto + metadatos de valoración. CurrentValue podrá dañarse más adelante.
/// </summary>
public sealed class PhotoShot
{
    public Texture2D Texture { get; }
    public PhotoEvaluation Evaluation { get; }
    public Transform Owner { get; }

    public PhotoShot(Texture2D texture, PhotoEvaluation evaluation, Transform owner)
    {
        Texture = texture;
        Evaluation = evaluation ?? new PhotoEvaluation();
        Owner = owner;
    }

    public bool IsControversialActionShot => Evaluation != null && Evaluation.IsControversialActionShot;
}
