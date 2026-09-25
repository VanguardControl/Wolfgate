using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.PlanetCracker.Cracker;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>Gives a cracker its anchor transport, loaded beside the hull and docked onto it.</summary>
public sealed partial class WFCrackerOwnershipSystem
{
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    /// <summary>Tiles of clearance left between the hull's own footprint and where the transport is parked.</summary>
    private const float TransportGap = 12f;

    /// <summary>Loads this hull's transport beside it and docks it; null if it has none or loading failed.</summary>
    public EntityUid? TrySpawnTransport(Entity<WFPlanetCrackerComponent> cracker)
    {
        if (cracker.Comp.TransportSpawned || cracker.Comp.TransportMap is not { } path)
            return null;

        var xform = Transform(cracker.Owner);

        if (xform.MapID == MapId.Nullspace)
            return null;

        // Park it clear of the hull; the dock below moves it onto the airlock from wherever it lands.
        var width = TryComp<MapGridComponent>(cracker.Owner, out var grid) ? grid.LocalAABB.Width : 0f;
        var offset = _transform.GetWorldPosition(xform) + new Vector2(width + TransportGap, 0f);

        if (!_mapLoader.TryLoadGrid(xform.MapID, path, out var transport, offset: offset))
        {
            Log.Error($"Could not load the anchor transport {path} for {ToPrettyString(cracker)}.");
            return null;
        }

        // Set before docking, so a second purchase event cannot race a duplicate in.
        cracker.Comp.TransportSpawned = true;

        DockTransport(cracker.Owner, transport.Value.Owner);

        return transport.Value.Owner;
    }

    /// <summary>Docks a transport onto a cracker, or parks it alongside, and binds its anchors and crates.</summary>
    public bool DockTransport(EntityUid cracker, EntityUid transport)
    {
        var shuttle = EnsureComp<ShuttleComponent>(transport);
        var docked = _shuttle.TryFTLDock(transport, shuttle, cracker);

        if (!docked)
            Log.Warning($"No airlock pair lined up for {ToPrettyString(transport)}; it is parked beside {ToPrettyString(cracker)} instead.");

        // Docked or parked, the transport grid needs its own binding pass.
        BindAboard(cracker, transport);

        return docked;
    }
}
