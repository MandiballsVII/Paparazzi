using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public class NetworkPhysicsProp : NetworkBehaviour
{
    readonly SyncVar<Color> color = new(Color.white);

    public void SetColor(Color value)
    {
        if (!IsServerInitialized)
            return;
        color.Value = value;
        Apply(value);
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        color.OnChange += OnColorChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        color.OnChange -= OnColorChanged;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Apply(color.Value);
    }

    void OnColorChanged(Color prev, Color next, bool asServer)
    {
        Apply(next);
    }

    void Apply(Color c)
    {
        ArenaBuilder.ApplyColor(gameObject, c);
    }
}
