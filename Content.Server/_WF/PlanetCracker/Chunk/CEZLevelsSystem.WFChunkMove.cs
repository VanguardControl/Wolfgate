using System.Numerics;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._CE.ZLevels.Core;

/// <summary>
/// MoveGridSetToMap for one grid and a DESTINATION position, which the private original cannot take.
/// A partial rather than a helper because RaiseZMoveEventOnPassengers is private: without it the chunk's riders keep
/// their old CurrentZLevel and ground-height caches and read the ground layer from orbit.
/// Declares no subscriptions and re-declares no dependency.
/// </summary>
public sealed partial class CEZLevelsSystem
{
    /// <summary>Moves one grid onto another z-layer at a given world pose, raising the same events a convoy move does.</summary>
    /// <param name="grid">The grid to move.</param>
    /// <param name="targetMap">The layer map it lands on.</param>
    /// <param name="worldPos">Where it lands, in world terms.</param>
    /// <param name="worldRot">The world rotation it keeps.</param>
    /// <param name="offset">Levels crossed; positive is upward.</param>
    /// <param name="depth">The destination layer's depth.</param>
    public void WfMoveGridToLayer(
        Entity<MapGridComponent> grid,
        EntityUid targetMap,
        Vector2 worldPos,
        Angle worldRot,
        int offset,
        int depth)
    {
        var beforeEv = new CEZLevelBeforeMapMoveEvent(offset, depth);
        RaiseLocalEvent(grid.Owner, ref beforeEv);

        var xform = Transform(grid.Owner);

        // The map change wipes joints and can reset momentum, so save and restore it, as MoveGridSetToMap does.
        var linVel = Vector2.Zero;
        var angVel = 0f;
        TryComp<PhysicsComponent>(grid.Owner, out var body);

        if (body != null)
        {
            linVel = body.LinearVelocity;
            angVel = body.AngularVelocity;
        }

        WfDestroyRiderContacts(grid.Owner);
        _transform.SetCoordinates(grid.Owner, xform, new EntityCoordinates(targetMap, worldPos), rotation: worldRot);

        if (body != null)
        {
            _physics.SetLinearVelocity(grid.Owner, linVel, body: body);
            _physics.SetAngularVelocity(grid.Owner, angVel, body: body);
        }

        // HandleMapChange clears every joint in the subtree on the re-parent, so the docks have to be remade.
        _dockSystem.RedockDocks(grid.Owner);
        _console.RefreshShuttleConsoles(grid.Owner);

        var ev = new CEZLevelMapMoveEvent(offset, depth);
        RaiseLocalEvent(grid.Owner, ref ev);
        RaiseZMoveEventOnPassengers(grid.Owner, ref ev);
    }
}
