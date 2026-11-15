using UnityEngine;

/// <summary>
/// Identidad de sesión del jugador (índice, color, viewport) para sobrevivir al Lobby.
/// </summary>
[DisallowMultipleComponent]
public class PlayerSessionIdentity : MonoBehaviour
{
    [SerializeField]
    int playerIndex;

    [SerializeField]
    int colorIndex;

    [SerializeField]
    Rect viewport = new Rect(0f, 0f, 1f, 1f);

    [SerializeField]
    string controlScheme = string.Empty;

    public int PlayerIndex => playerIndex;
    public int ColorIndex => colorIndex;
    public Rect Viewport => viewport;
    public string ControlScheme => controlScheme;

    public void Configure(int index, int color, Rect view, string scheme)
    {
        playerIndex = index;
        colorIndex = Mathf.Clamp(color, 0, ArenaBuilder.PlayerColors.Length - 1);
        viewport = view;
        controlScheme = scheme ?? string.Empty;
        ApplyColor();
    }

    public void SetViewport(Rect view)
    {
        viewport = view;
    }

    public void ApplyColor()
    {
        ArenaBuilder.ApplyColor(gameObject, ArenaBuilder.PlayerColors[colorIndex]);
    }
}
