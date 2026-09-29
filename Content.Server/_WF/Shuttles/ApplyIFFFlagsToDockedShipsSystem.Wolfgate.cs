using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;

namespace Content.Server._Mono.Detection;

public sealed partial class ApplyIFFFlagsToDockedShipsSystem
{
    /// <summary>
    /// Adds the host's flags to a ship, or takes back the ones it added once no port holds the ship to the host.
    /// Flags the ship had before docking stay.
    /// </summary>
    private void ApplyTrackedFlags(EntityUid gridUid, ApplyIFFFlagsToDockedShipsComponent host, bool applying)
    {
        if (applying)
        {
            var before = CompOrNull<IFFComponent>(gridUid)?.Flags ?? IFFFlags.None;
            _shuttle.AddIFFFlag(gridUid, host.Flags);
            var gained = (CompOrNull<IFFComponent>(gridUid)?.Flags ?? IFFFlags.None) & ~before;
            host.AddedFlags[gridUid] = host.AddedFlags.GetValueOrDefault(gridUid) | gained;
            return;
        }

        if (IsDockedTo(gridUid, host) || !host.AddedFlags.Remove(gridUid, out var added))
            return;

        _shuttle.RemoveIFFFlag(gridUid, added);
    }

    /// <summary>
    /// Whether any port on the grid is still docked to a grid carrying this host component.
    /// </summary>
    private bool IsDockedTo(EntityUid gridUid, ApplyIFFFlagsToDockedShipsComponent host)
    {
        // Not DockingSystem.GetDocks: it refills the set UndockDocks may be iterating while this runs.
        var query = EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (query.MoveNext(out var dock, out var xform))
        {
            if (xform.GridUid != gridUid ||
                dock.DockedWith is not { } other ||
                !TryComp(other, out TransformComponent? otherXform))
            {
                continue;
            }

            if (TryComp<ApplyIFFFlagsToDockedShipsComponent>(otherXform.GridUid, out var otherHost) && otherHost == host)
                return true;
        }

        return false;
    }
}
