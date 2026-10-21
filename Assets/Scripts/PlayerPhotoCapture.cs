using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(PlayerPhotoAlbum))]
public class PlayerPhotoCapture : MonoBehaviour
{
    [SerializeField] int photoWidth = 640;
    [SerializeField] int photoHeight = 360;
    [SerializeField] Camera playerCamera;

    PlayerInput playerInput;
    PlayerPhotoAlbum album;
    PlayerPauseMenu pauseMenu;
    InputAction photoAction;
    bool inputBound;

    public void Initialize(Camera camera)
    {
        playerCamera = camera;
    }

    void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        album = GetComponent<PlayerPhotoAlbum>();
        TryGetComponent(out pauseMenu);

        if (playerCamera == null)
        {
            Transform pivot = transform.Find("CameraPivot");
            if (pivot != null)
                playerCamera = pivot.GetComponentInChildren<Camera>(true);
        }
    }

    void OnEnable()
    {
        BindInput();
    }

    void OnDisable()
    {
        UnbindInput();
    }

    public void BindInput()
    {
        if (inputBound || playerInput == null || playerInput.actions == null)
            return;

        photoAction = playerInput.actions["Photo"];
        if (photoAction == null)
            return;

        photoAction.performed += OnPhotoPerformed;
        inputBound = true;
    }

    public void UnbindInput()
    {
        if (!inputBound || photoAction == null)
            return;

        photoAction.performed -= OnPhotoPerformed;
        inputBound = false;
    }

    void OnPhotoPerformed(InputAction.CallbackContext context)
    {
        if (pauseMenu == null)
            TryGetComponent(out pauseMenu);

        if (pauseMenu != null && pauseMenu.IsOpen)
            return;

        CaptureAndStore();
    }

    public Texture2D CaptureAndStore()
    {
        Texture2D photo = CaptureFrame();
        if (photo != null)
            album.AddPhoto(photo);
        return photo;
    }

    Texture2D CaptureFrame()
    {
        if (playerCamera == null || !playerCamera.enabled)
            return null;

        int width = Mathf.Max(16, photoWidth);
        int height = Mathf.Max(16, photoHeight);

        RenderTexture temporary = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = playerCamera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        Rect previousRect = playerCamera.rect;
        bool previousEnabled = playerCamera.enabled;

        // En split-screen, camera.rect es media pantalla: si no lo reseteamos,
        // Render() solo rellena esa mitad del RT y el resto queda negro.
        playerCamera.enabled = true;
        playerCamera.rect = new Rect(0f, 0f, 1f, 1f);
        playerCamera.targetTexture = temporary;
        playerCamera.Render();

        RenderTexture.active = temporary;
        Texture2D photo = new Texture2D(width, height, TextureFormat.RGB24, false);
        photo.name = $"Photo_{album.Count + 1}";
        photo.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
        photo.Apply(false, false);

        playerCamera.targetTexture = previousTarget;
        playerCamera.rect = previousRect;
        RenderTexture.active = previousActive;
        playerCamera.enabled = previousEnabled;
        RenderTexture.ReleaseTemporary(temporary);

        return photo;
    }
}
