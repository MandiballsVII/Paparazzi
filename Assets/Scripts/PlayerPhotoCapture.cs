using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(PlayerPhotoAlbum))]
public class PlayerPhotoCapture : MonoBehaviour
{
    [SerializeField] int photoWidth = 640;
    [SerializeField] int photoHeight = 360;
    [SerializeField] float maxSubjectDistance = 40f;
    [SerializeField] Camera playerCamera;

    PlayerInput playerInput;
    PlayerPhotoAlbum album;
    PlayerPauseMenu pauseMenu;
    PlayerHealth health;
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
        TryGetComponent(out health);

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

        if (health == null)
            TryGetComponent(out health);
        if (health != null && health.IsDead)
            return;

        CaptureAndStore();
    }

    public Texture2D CaptureAndStore()
    {
        Texture2D photo = CaptureFrame();
        if (photo == null)
            return null;

        CelebrityBrain celebrity = PhotoEvaluator.FindCelebrity();
        var context = new PhotoCaptureContext
        {
            Camera = playerCamera,
            Photographer = transform,
            Celebrity = celebrity,
            MaxSubjectDistance = maxSubjectDistance
        };

        PhotoEvaluation evaluation = PhotoEvaluator.Evaluate(context);
        var shot = new PhotoShot(photo, evaluation, transform);
        album.AddShot(shot);

        if (shot.IsControversialActionShot)
        {
            RoundObjectiveState.NotifyControversialActionShot(shot);
            if (celebrity != null)
            {
                Vector3 photographerPos = transform.position;
                if (celebrity.TryGetComponent(out NetworkCelebrity netCelebrity))
                    netCelebrity.RequestPhotographedDuringAction(photographerPos);
                else
                    celebrity.NotifyPhotographedDuringAction(photographerPos);
            }
        }

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
