public interface IPhotoFactor
{
    PhotoFactorId Id { get; }
    void Evaluate(in PhotoCaptureContext context, PhotoEvaluation evaluation);
}
