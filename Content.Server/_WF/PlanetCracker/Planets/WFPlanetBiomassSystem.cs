using Content.Server.Spreader;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Planets;

/// <summary>Allow chimera biomass on hulls, never streamed terrain or extracted chunks.</summary>
[RegisterComponent]
public sealed partial class WFPlanetBiomassRestrictionComponent : Component;

public sealed partial class WFPlanetBiomassSystem : EntitySystem
{
    public const int MaxHullBiomass = 256;
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _aboard = new();
    private readonly Dictionary<EntityUid, EntityUid> _grids = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WFPlanetBiomassRestrictionComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFPlanetBiomassRestrictionComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<WFPlanetBiomassRestrictionComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<WFPlanetBiomassRestrictionComponent, SpreadNeighborsEvent>(OnSpread, before: new[] { typeof(KudzuSystem) });
    }

    public bool IsPlanet(EntityUid uid)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return false;
        if (HasComp<WFPlanetChunkComponent>(uid) || HasComp<WFPlanetChunkComponent>(xform.GridUid))
            return true;
        if (HasComp<MapGridComponent>(uid) && !HasComp<MapComponent>(uid))
            return false;
        // A separate hull remains a legitimate host even when its map is a planet.
        if (xform.GridUid is { } grid && grid != xform.MapUid && HasComp<MapGridComponent>(grid))
            return false;
        return HasComp<WFPlanetLayerComponent>(uid) || HasComp<WFPlanetLayerComponent>(xform.MapUid);
    }

    public int HullCount(EntityUid grid) => _aboard.TryGetValue(grid, out var entities) ? entities.Count : 0;
    private void OnMapInit(Entity<WFPlanetBiomassRestrictionComponent> ent, ref MapInitEvent args) => Track(ent);
    private void OnParentChanged(Entity<WFPlanetBiomassRestrictionComponent> ent, ref EntParentChangedMessage args) => Track(ent);
    private void OnTerminating(Entity<WFPlanetBiomassRestrictionComponent> ent, ref EntityTerminatingEvent args) => Forget(ent);

    private void Forget(EntityUid uid)
    {
        if (_grids.Remove(uid, out var grid) && _aboard.TryGetValue(grid, out var entities))
        {
            entities.Remove(uid);
            if (entities.Count == 0) _aboard.Remove(grid);
        }
    }

    private void Track(EntityUid uid)
    {
        Forget(uid);
        if (IsPlanet(uid))
        {
            QueueDel(uid);
            return;
        }
        if (Transform(uid).GridUid is not { } grid || !HasComp<MapGridComponent>(grid))
            return;
        if (!_aboard.TryGetValue(grid, out var entities))
            _aboard[grid] = entities = new();
        if (entities.Count >= MaxHullBiomass)
        {
            QueueDel(uid);
            return;
        }
        entities.Add(uid);
        _grids[uid] = grid;
    }

    private void OnSpread(Entity<WFPlanetBiomassRestrictionComponent> ent, ref SpreadNeighborsEvent args)
    {
        if (IsPlanet(ent) || Transform(ent).GridUid is not { } grid)
        {
            args.NeighborFreeTiles.Clear();
            return;
        }
        var room = Math.Max(0, MaxHullBiomass - HullCount(grid));
        // Native kudzu supplies closed-door/wall checks. Keep its candidates on this hull,
        // so growth cannot escape through docking connections onto terrain or another grid.
        args.NeighborFreeTiles.RemoveAll(tile => tile.Tile.GridUid != grid || tile.Tile.Tile.IsEmpty);
        if (args.NeighborFreeTiles.Count > room)
            args.NeighborFreeTiles.RemoveRange(room, args.NeighborFreeTiles.Count - room);
    }
}
