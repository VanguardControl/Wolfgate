using Content.Shared._WF.MappingMode;
using Content.Shared.Ghost;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Player;
using EngineMapSystem = Robust.Server.GameObjects.MapSystem;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    private void InitializeRiders()
    {
        // The engine deletes an emptied grid with everything on it; observers step off first.
        SubscribeLocalEvent<EmptyGridEvent>(OnGridEmpty, before: [typeof(EngineMapSystem)]);
    }

    private void OnGridEmpty(EmptyGridEvent ev)
    {
        if (Enabled)
            MoveRidersOff(ev.GridId);
    }

    /// <summary>
    /// Moves mapping observers, players and ghosts standing on a grid onto its map where they are, so deleting the
    /// grid doesn't take them along.
    /// </summary>
    public void MoveRidersOff(EntityUid grid)
    {
        if (!HasComp<MapGridComponent>(grid) || Transform(grid).MapUid is not { } map || map == grid)
            return;

        var riders = new List<EntityUid>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<WFMappingObserverComponent>(child) || HasComp<ActorComponent>(child) || HasComp<GhostComponent>(child))
                riders.Add(child);
        }

        foreach (var uid in riders)
        {
            var (position, rotation) = _transform.GetWorldPositionRotation(uid);
            _transform.SetCoordinates(uid, Transform(uid), new EntityCoordinates(map, position), rotation);
        }
    }
}
