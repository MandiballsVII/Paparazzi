using UnityEngine;

/// <summary>
/// Ruido al chocar props contra suelo/paredes/otros cuerpos.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class ImpactNoiseEmitter : MonoBehaviour
{
    [SerializeField]
    float loudnessPerSpeed = 0.18f;

    [SerializeField]
    float minRelativeSpeed = 1.5f;

    [SerializeField]
    float maxLoudness = 2f;

    [SerializeField]
    float cooldown = 0.12f;

    float nextAllowedTime;

    void OnCollisionEnter(Collision collision)
    {
        if (Time.time < nextAllowedTime)
            return;
        if (collision == null || collision.contactCount == 0)
            return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < minRelativeSpeed)
            return;

        float loudness = Mathf.Min(maxLoudness, speed * loudnessPerSpeed);
        ContactPoint contact = collision.GetContact(0);
        NoiseHub.Emit(contact.point, loudness, NoiseType.Impact, transform);
        nextAllowedTime = Time.time + cooldown;
    }
}
