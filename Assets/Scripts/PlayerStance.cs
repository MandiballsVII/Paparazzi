using UnityEngine;

/// <summary>
/// Stance de pie / agachado: CharacterController, cámara y mesh visual.
/// Los pies se mantienen anclados (solo baja altura y centro).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerStance : MonoBehaviour
{
    [SerializeField]
    float standHeight = 2f;

    [SerializeField]
    float crouchHeight = 1f;

    [SerializeField]
    float standCameraY = 0.75f;

    [SerializeField]
    float crouchCameraY = 0.15f;

    [SerializeField]
    float crouchMoveMultiplier = 0.5f;

    [SerializeField]
    Transform cameraPivot;

    [SerializeField]
    Transform bodyVisual;

    [SerializeField]
    LayerMask ceilingMask = ~0;

    CharacterController controller;
    bool isCrouching;

    public bool IsCrouching => isCrouching;
    public float MoveMultiplier => isCrouching ? crouchMoveMultiplier : 1f;

    public void Initialize(Transform pivot)
    {
        if (controller == null)
            controller = GetComponent<CharacterController>();
        cameraPivot = pivot;
        EnsureBodyVisual();
        ApplyStance(false, force: true);
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        EnsureBodyVisual();

        if (cameraPivot == null)
        {
            Transform found = transform.Find("CameraPivot");
            if (found != null)
                cameraPivot = found;
        }
    }

    void Start()
    {
        // Por si Initialize no se llamó (prefab de red).
        if (bodyVisual == null)
            EnsureBodyVisual();
        ApplyStance(isCrouching, force: true);
    }

    /// <param name="ignoreCeiling">True al replicar por red (otros clientes / SyncVar).</param>
    public void SetCrouching(bool crouch, bool ignoreCeiling = false)
    {
        if (crouch == isCrouching)
            return;

        if (!crouch && !ignoreCeiling && !CanStandUp())
            return;

        ApplyStance(crouch, force: false);
    }

    void ApplyStance(bool crouch, bool force)
    {
        if (controller == null)
            return;
        if (!force && crouch == isCrouching)
            return;

        isCrouching = crouch;

        float height = isCrouching ? crouchHeight : standHeight;
        // Mantener la base del collider en los pies (y local = -standHeight/2).
        float centerY = -0.5f * (standHeight - height);

        controller.height = height;
        controller.center = new Vector3(0f, centerY, 0f);
        controller.radius = 0.5f;

        if (cameraPivot != null)
        {
            Vector3 camPos = cameraPivot.localPosition;
            camPos.y = isCrouching ? crouchCameraY : standCameraY;
            cameraPivot.localPosition = camPos;
        }

        if (bodyVisual != null)
        {
            float scaleY = height / standHeight;
            bodyVisual.localScale = new Vector3(1f, scaleY, 1f);
            // Compensa el escalado desde el centro para que los pies no floten.
            bodyVisual.localPosition = new Vector3(0f, centerY, 0f);
        }
    }

    bool CanStandUp()
    {
        if (controller == null)
            return true;

        float radius = Mathf.Max(0.05f, controller.radius * 0.9f);
        float standTop = standHeight * 0.5f;
        float crouchTop = transform.position.y + controller.center.y + controller.height * 0.5f;
        float originY = crouchTop - radius;
        float castDistance = Mathf.Max(0.01f, standTop - (originY - transform.position.y) - radius);

        Vector3 origin = new Vector3(transform.position.x, originY, transform.position.z);
        return !Physics.SphereCast(
            origin,
            radius,
            Vector3.up,
            out _,
            castDistance,
            ceilingMask,
            QueryTriggerInteraction.Ignore
        );
    }

    void EnsureBodyVisual()
    {
        if (bodyVisual == null)
        {
            Transform existing = transform.Find("Body");
            if (existing != null)
                bodyVisual = existing;
        }

        if (bodyVisual == null)
        {
            if (!TryGetComponent(out MeshFilter rootFilter) || !TryGetComponent(out MeshRenderer rootRenderer))
                return;

            var bodyGo = new GameObject("Body");
            bodyVisual = bodyGo.transform;
            bodyVisual.SetParent(transform, false);
            bodyVisual.localPosition = Vector3.zero;
            bodyVisual.localRotation = Quaternion.identity;
            bodyVisual.localScale = Vector3.one;

            MeshFilter bodyFilter = bodyGo.AddComponent<MeshFilter>();
            bodyFilter.sharedMesh = rootFilter.sharedMesh;

            MeshRenderer bodyRenderer = bodyGo.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterials = rootRenderer.sharedMaterials;
        }

        // Quitar mesh del root de inmediato (Destroy diferido dejaba la cápsula gris encima).
        StripRootMesh();
    }

    void StripRootMesh()
    {
        if (TryGetComponent(out MeshRenderer rootRenderer))
        {
            rootRenderer.enabled = false;
            DestroyImmediate(rootRenderer);
        }

        if (TryGetComponent(out MeshFilter rootFilter))
            DestroyImmediate(rootFilter);
    }
}
