/// <summary>
/// Factores de valoración de una foto. Ampliar aquí cuando entren foco, luz, encuadre, etc.
/// </summary>
public enum PhotoFactorId : byte
{
    CelebrityInFrame = 0,
    CelebrityPerformingAction = 1,
    // Futuro:
    // SubjectCentered = 2,
    // SubjectFullyVisible = 3,
    // SharpFocus = 4,
    // WellLit = 5,
    // MotionBlur = 6,
}
