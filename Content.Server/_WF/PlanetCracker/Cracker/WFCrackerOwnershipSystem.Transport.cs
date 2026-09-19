using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.PlanetCracker.Cracker;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>
/// Gives a cracker its anchor transport: loaded beside the hull and docked onto it, so the pair never has to be
/// spawned separately. Shared by the shipyard purchase and the admin-built test hull.
/// </summary>
public sealed partial class WFCrackerOwnershipSystem
{
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    /// <summary>Tiles of clearance left between the hull's own footprint and where the transport is parked.</summary>
    private const float TransportGap = 12f;

    /// <summary>
    /// Loads this hull's transport beside it and docks it on. Returns the transport grid, or null when the hull ships
    /// without one or the file could not be loaded.
    /// </summary>
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

    /// <summary>
    /// Docks a transport onto a cracker's airlock and stamps its anchors and crates with that cracker. A hull with no
    /// free matching airlock leaves the transport parked alongside instead, which TryFTLDock handles for us.
    /// </summary>
    public bool DockTransport(EntityUid cracker, EntityUid transport)
    {
        var shuttle = EnsureComp<ShuttleComponent>(transport);
        var docked = _shuttle.TryFTLDock(transport, shuttle, cracker);

        if (!docked)
            Log.Warning($"No airlock pair lined up for {ToPrettyString(transport)}; it is parked beside {ToPrettyString(cracker)} instead.");

        // Docked or merely parked, the transport's crate belongs to this cracker: BindAboard walks the cracker's own
        // deck only, so the transport grid needs its own pass.
        BindAboard(cracker, transport);

        return docked;
    }
}
