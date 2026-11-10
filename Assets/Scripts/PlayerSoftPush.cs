using UnityEngine;

/// <summary>
/// Empujes suaves entre CharacterControllers y contra rigidbodies dinámicos.
/// </summary>
public static class PlayerSoftPush
{
    static readonly Collider[] OverlapBuffer = new Collider[24];

    public static void SeparateFromNearbyPlayers(
        CharacterController self,
        float probeRadius,
        float pushSpeed,
        float deltaTime)
    {
        if (self == null || pushSpeed <= 0f || deltaTime <= 0f)
            return;

        Vector3 center = self.transform.TransformPoint(self.center);
        int count = Physics.OverlapSphereNonAlloc(
            center,
            probeRadius,
            OverlapBuffer,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float step = pushSpeed * deltaTime;
        for (int i = 0; i < count; i++)
        {
            Collider col = OverlapBuffer[i];
            if (col == null)
                continue;

            CharacterController other = col as CharacterController;
            if (other == null)
                other = col.GetComponentInParent<CharacterController>();
            if (other == null || other == self)
                continue;

            Vector3 away = self.transform.position - other.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = self.transform.forward;
            away.Normalize();

            // Cada owner se empuja a sí mismo → funciona en local y online.
            self.Move(away * step);
        }
    }

    public static void SoftPushDynamicBody(
        Rigidbody body,
        Vector3 fromPoint,
        float acceleration,
        float maxSpeed)
    {
        if (body == null || body.isKinematic || acceleration <= 0f)
            return;

        Vector3 away = body.worldCenterOfMass - fromPoint;
        if (away.sqrMagnitude < 0.0001f)
            away = Vector3.forward;
        away.Normalize();

        body.AddForce(away * acceleration, ForceMode.Acceleration);

        Vector3 v = body.linearVelocity;
        float speed = v.magnitude;
        if (speed > maxSpeed && speed > 0.001f)
            body.linearVelocity = v * (maxSpeed / speed);
    }

    public static void SoftPushDynamicsAround(
        Vector3 center,
        float probeRadius,
        float acceleration,
        float maxSpeed,
        Rigidbody ignoreBody = null)
    {
        int count = Physics.OverlapSphereNonAlloc(
            center,
            probeRadius,
            OverlapBuffer,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider col = OverlapBuffer[i];
            if (col == null)
                continue;

            Rigidbody rb = col.attachedRigidbody;
            if (rb == null || rb == ignoreBody || rb.isKinematic)
                continue;
            if (rb.GetComponentInParent<CharacterController>() != null)
                continue;

            SoftPushDynamicBody(rb, center, acceleration, maxSpeed);
        }
    }
}
