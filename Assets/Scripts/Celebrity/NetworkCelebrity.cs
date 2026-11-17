using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Famoso en red: IA solo en server; clientes son puppets (SyncVars + NetworkTransform).
/// </summary>
[RequireComponent(typeof(CelebrityBrain))]
[DefaultExecutionOrder(-25)]
public class NetworkCelebrity : NetworkBehaviour
{
    readonly SyncVar<byte> syncedState = new((byte)CelebrityBrain.AiState.Patrol);
    readonly SyncVar<float> syncedCooldown = new();
    readonly SyncVar<float> syncedActionTime = new();
    readonly SyncVar<bool> syncedHidden = new();
    readonly SyncVar<bool> syncedPermanentFlee = new();
    readonly SyncVar<int> syncedActionIndex = new(-1);
    readonly SyncVar<bool> syncedHasLocation = new();
    readonly SyncVar<Vector3> syncedLocationPoint = new();

    CelebrityBrain brain;
    NetworkTransform networkTransform;
    float nextPushTime;
    const float SyncPushInterval = 0.1f;

    void Awake()
    {
        brain = GetComponent<CelebrityBrain>();
        TryGetComponent(out networkTransform);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        PushRoundSelectionFromServer();
        brain.BeginAuthorityBrain();
        PushSyncFromBrain(force: true);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyRoundSelectionFromSync();

        if (!IsServerInitialized)
            brain.SetPuppetMode(true);

        syncedState.OnChange += OnSyncedChanged;
        syncedCooldown.OnChange += OnSyncedChanged;
        syncedActionTime.OnChange += OnSyncedChanged;
        syncedHidden.OnChange += OnSyncedChanged;
        syncedPermanentFlee.OnChange += OnSyncedChanged;
        syncedActionIndex.OnChange += OnActionIndexChanged;
        syncedHasLocation.OnChange += OnLocationChanged;
        syncedLocationPoint.OnChange += OnLocationChanged;

        ApplyAllToBrain();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        syncedState.OnChange -= OnSyncedChanged;
        syncedCooldown.OnChange -= OnSyncedChanged;
        syncedActionTime.OnChange -= OnSyncedChanged;
        syncedHidden.OnChange -= OnSyncedChanged;
        syncedPermanentFlee.OnChange -= OnSyncedChanged;
        syncedActionIndex.OnChange -= OnActionIndexChanged;
        syncedHasLocation.OnChange -= OnLocationChanged;
        syncedLocationPoint.OnChange -= OnLocationChanged;
    }

    void LateUpdate()
    {
        if (!IsServerInitialized || brain == null || brain.IsPuppet)
            return;

        if (Time.time < nextPushTime)
            return;

        nextPushTime = Time.time + SyncPushInterval;
        PushSyncFromBrain(force: false);
    }

    void PushRoundSelectionFromServer()
    {
        CelebrityRoundState round = CelebrityRoundState.GetOrCreate();
        round.EnsureSelection();

        syncedActionIndex.Value = CelebrityActionCatalog.IndexOf(round.SelectedAction.Id);
        syncedHasLocation.Value = round.HasLocationPoint;
        syncedLocationPoint.Value = round.LocationPoint;
    }

    void ApplyRoundSelectionFromSync()
    {
        if (syncedActionIndex.Value < 0)
            return;

        CelebrityRoundState round = CelebrityRoundState.GetOrCreate();
        round.ApplyNetworkSelection(
            syncedActionIndex.Value,
            syncedHasLocation.Value,
            syncedLocationPoint.Value);
    }

    void PushSyncFromBrain(bool force)
    {
        byte state = (byte)brain.CurrentState;
        if (force || syncedState.Value != state)
        {
            syncedState.Value = state;
            // Huida / hide hacen Warp grandes: forzar teleport en NT.
            if (state == (byte)CelebrityBrain.AiState.Flee
                || state == (byte)CelebrityBrain.AiState.Hidden)
                networkTransform?.Teleport();
        }

        if (force || !Mathf.Approximately(syncedCooldown.Value, brain.ActionCooldownRemaining))
            syncedCooldown.Value = brain.ActionCooldownRemaining;
        if (force || !Mathf.Approximately(syncedActionTime.Value, brain.ActionTimeRemaining))
            syncedActionTime.Value = brain.ActionTimeRemaining;

        bool hidden = brain.CurrentState == CelebrityBrain.AiState.Hidden || brain.HasFledPermanently;
        if (force || syncedHidden.Value != hidden)
            syncedHidden.Value = hidden;
        if (force || syncedPermanentFlee.Value != brain.HasFledPermanently)
            syncedPermanentFlee.Value = brain.HasFledPermanently;
    }

    void ApplyAllToBrain()
    {
        ApplyRoundSelectionFromSync();
        brain.ApplyNetworkState(
            (CelebrityBrain.AiState)syncedState.Value,
            syncedCooldown.Value,
            syncedActionTime.Value,
            syncedHidden.Value,
            syncedPermanentFlee.Value);
    }

    void OnSyncedChanged(byte prev, byte next, bool asServer)
    {
        if (!asServer)
            ApplyAllToBrain();
    }

    void OnSyncedChanged(float prev, float next, bool asServer)
    {
        if (!asServer)
            ApplyAllToBrain();
    }

    void OnSyncedChanged(bool prev, bool next, bool asServer)
    {
        if (!asServer)
            ApplyAllToBrain();
    }

    void OnActionIndexChanged(int prev, int next, bool asServer)
    {
        if (!asServer)
            ApplyRoundSelectionFromSync();
    }

    void OnLocationChanged(bool prev, bool next, bool asServer)
    {
        if (!asServer)
            ApplyRoundSelectionFromSync();
    }

    void OnLocationChanged(Vector3 prev, Vector3 next, bool asServer)
    {
        if (!asServer)
            ApplyRoundSelectionFromSync();
    }

    public void RequestPhotographedDuringAction()
    {
        if (IsServerInitialized)
        {
            brain.NotifyPhotographedDuringAction();
            PushSyncFromBrain(force: true);
            return;
        }

        PhotographedDuringActionServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    void PhotographedDuringActionServerRpc()
    {
        brain.NotifyPhotographedDuringAction();
        PushSyncFromBrain(force: true);
    }
}
