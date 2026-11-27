using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Capa de red del agarre. La mecánica vive en <see cref="PlayerGrab"/>.
/// </summary>
[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(PlayerGrab))]
public class NetworkPlayerGrab : NetworkBehaviour
{
    readonly SyncVar<bool> isHolding = new();
    readonly SyncVar<NetworkObject> heldSync = new();

    PlayerGrab grab;
    NetworkObject heldObject;
    bool wasKinematic;
    bool usedGravity;
    bool hooksWired;

    public bool IsHolding => isHolding.Value;

    void Awake()
    {
        EnsureGrab();
        // Hasta OnStartClient owner: sin input de agarre.
        grab.GrabActive = false;
        grab.ClearNetworkHandlers();

        isHolding.OnChange += OnIsHoldingChanged;
        heldSync.OnChange += OnHeldSyncChanged;
    }

    void OnDestroy()
    {
        isHolding.OnChange -= OnIsHoldingChanged;
        heldSync.OnChange -= OnHeldSyncChanged;
    }

    void EnsureGrab()
    {
        if (!TryGetComponent(out grab))
            grab = gameObject.AddComponent<PlayerGrab>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner)
            WireOwnerHooks();
        else
        {
            grab.GrabActive = false;
            grab.ClearNetworkHandlers();
        }

        SyncLocalHoldFromVars();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        UnwireOwnerHooks();
        grab.ReleaseHold(Vector3.zero, restorePhysics: false);
        if (IsServerInitialized)
            ForceDrop(Vector3.zero);
    }

    void WireOwnerHooks()
    {
        if (hooksWired || grab == null)
            return;

        grab.NetworkGrabHandler = TryNetworkGrab;
        grab.NetworkDropHandler = impulse => DropServerRpc(impulse);
        grab.NetworkHoldSyncHandler = (pos, rot) => UpdateHoldServerRpc(pos, rot);
        grab.SoftPushDynamicHandler = SoftPushDynamicFromMotor;
        grab.GrabActive = true;
        hooksWired = true;
    }

    void UnwireOwnerHooks()
    {
        if (!hooksWired || grab == null)
            return;

        grab.ClearNetworkHandlers();
        grab.GrabActive = false;
        hooksWired = false;
    }

    bool TryNetworkGrab(RaycastHit hit)
    {
        if (hit.rigidbody == null)
            return true;

        bool isDeadPlayer = hit.rigidbody.TryGetComponent(out PlayerHealth targetHealth) && targetHealth.IsDead;
        if (!isDeadPlayer && hit.rigidbody.mass > grab.MaxGrabMass)
            return true;

        if (!hit.rigidbody.TryGetComponent(out NetworkObject nob) || !nob.IsSpawned)
            return true;

        if (nob == NetworkObject)
            return true;

        GrabServerRpc(nob);
        return true;
    }

    void SoftPushDynamicFromMotor(Rigidbody body, Vector3 fromPoint)
    {
        if (body == null || body.isKinematic)
            return;
        if (!body.TryGetComponent(out NetworkObject nob) || !nob.IsSpawned)
            return;

        SoftPushDynamicServerRpc(nob, fromPoint);
    }

    void OnIsHoldingChanged(bool prev, bool next, bool asServer)
    {
        if (asServer)
            return;
        SyncLocalHoldFromVars();
    }

    void OnHeldSyncChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        if (asServer)
            return;
        SyncLocalHoldFromVars();
    }

    void SyncLocalHoldFromVars()
    {
        if (grab == null)
            return;

        if (!isHolding.Value || heldSync.Value == null)
        {
            if (grab.IsHolding)
                grab.ReleaseHold(Vector3.zero, restorePhysics: false);
            return;
        }

        if (!heldSync.Value.TryGetComponent(out Rigidbody body))
            return;

        heldSync.Value.TryGetComponent(out Collider col);
        grab.AttachHold(body, col, configurePhysics: false);
    }

    [ServerRpc]
    void GrabServerRpc(NetworkObject target, NetworkConnection caller = null)
    {
        if (target == null || !target.IsSpawned)
            return;

        if (!target.TryGetComponent(out Rigidbody body))
            return;

        bool isDeadPlayer = target.TryGetComponent(out PlayerHealth targetHealth) && targetHealth.IsDead;
        if (!isDeadPlayer && body.mass > grab.MaxGrabMass)
            return;

        if (heldObject != null)
            ForceDrop(Vector3.zero);

        heldObject = target;
        heldSync.Value = target;
        wasKinematic = body.isKinematic;
        usedGravity = body.useGravity;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;

        isHolding.Value = true;

        if (IsOwner && grab != null)
        {
            target.TryGetComponent(out Collider col);
            grab.AttachHold(body, col, configurePhysics: false);
        }
    }

    [ServerRpc]
    void DropServerRpc(Vector3 impulse)
    {
        ForceDrop(impulse);
    }

    [ServerRpc]
    void UpdateHoldServerRpc(Vector3 position, Quaternion rotation)
    {
        if (heldObject == null || !heldObject.TryGetComponent(out Rigidbody body))
            return;

        body.MovePosition(position);
        body.MoveRotation(rotation);
        body.transform.SetPositionAndRotation(position, rotation);
    }

    [ServerRpc]
    void SoftPushDynamicServerRpc(NetworkObject target, Vector3 fromPoint)
    {
        if (target == null || !target.TryGetComponent(out Rigidbody body) || body.isKinematic)
            return;

        PlayerSoftPush.SoftPushDynamicBody(
            body,
            fromPoint,
            grab != null ? grab.SoftPushAcceleration : 16f,
            grab != null ? grab.MaxSoftPushSpeed : 3f);
    }

    void ForceDrop(Vector3 impulse)
    {
        if (heldObject != null && heldObject.TryGetComponent(out Rigidbody body))
        {
            bool revivedPlayer = heldObject.TryGetComponent(out PlayerHealth heldHealth) && !heldHealth.IsDead;
            if (revivedPlayer)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
            else
            {
                body.isKinematic = wasKinematic;
                body.useGravity = usedGravity;

                if (!body.isKinematic && impulse.sqrMagnitude > 0f)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.AddForce(impulse, ForceMode.VelocityChange);
                }
            }
        }

        heldObject = null;
        if (IsServerInitialized)
        {
            heldSync.Value = default;
            isHolding.Value = false;
        }

        if (grab != null && grab.IsHolding)
            grab.ReleaseHold(Vector3.zero, restorePhysics: false);
    }
}
