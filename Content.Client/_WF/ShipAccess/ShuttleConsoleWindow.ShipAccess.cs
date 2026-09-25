using Robust.Shared.Network;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>The owner flipped the ship lock on the access tab.</summary>
    public event Action<bool>? ShipAccessLockedRequested;

    /// <summary>The owner wants a nearby person on the allow list.</summary>
    public event Action<NetEntity>? ShipAccessAddRequested;

    /// <summary>The owner wants a person off the allow list.</summary>
    public event Action<NetUserId>? ShipAccessRemoveRequested;

    /// <summary>The owner marked or unmarked a listed person as a builder.</summary>
    public event Action<NetUserId, bool>? ShipAccessBuilderRequested;

    /// <summary>The viewer wants to adopt an unowned ship.</summary>
    public event Action? ShipAccessClaimRequested;

    private void WfAccessInitialize()
    {
        AccessContainer.LockedChanged += locked => ShipAccessLockedRequested?.Invoke(locked);
        AccessContainer.AddRequested += target => ShipAccessAddRequested?.Invoke(target);
        AccessContainer.RemoveRequested += userId => ShipAccessRemoveRequested?.Invoke(userId);
        AccessContainer.BuilderChanged += (userId, builder) => ShipAccessBuilderRequested?.Invoke(userId, builder);
        AccessContainer.ClaimRequested += () => ShipAccessClaimRequested?.Invoke();
    }

    private void WfSetAccessMode(bool active)
    {
        AccessContainer.Visible = active;
    }

    private void WfAccessUpdateState(EntityUid? shuttle, EntityUid console)
    {
        AccessContainer.SetShuttle(shuttle);
        AccessContainer.SetConsole(console);
    }
}
