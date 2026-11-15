using UnityEngine;

public static class PlayerInteractUtility
{
    public static bool TryGetInteractable(
        Camera camera,
        float maxDistance,
        out IPlayerInteractable interactable,
        out float hitDistance)
    {
        interactable = null;
        hitDistance = 0f;
        if (camera == null)
            return false;

        Ray ray = new Ray(camera.transform.position, camera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;

        hitDistance = hit.distance;
        interactable = hit.collider.GetComponentInParent<IPlayerInteractable>();
        if (interactable == null)
            return false;

        if (hit.distance > interactable.InteractDistance)
        {
            interactable = null;
            return false;
        }

        return true;
    }
}
