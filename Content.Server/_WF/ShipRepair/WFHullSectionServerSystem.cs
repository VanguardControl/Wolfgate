using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WF.ShipRepair;
using Robust.Server.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ShipRepair;

/// <summary>Keeps one repair snapshot per broken hull and hands it on when the hull goes.</summary>
public sealed partial class WFHullSectionServerSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
        SubscribeLocalEvent<ShipRepairDataComponent, EntityTerminatingEvent>(OnHullTerminating);
        SubscribeLocalEvent<ShipRepairDataComponent, ComponentRemove>(OnSnapshotRemoved);
    }

    /// <summary>The biggest piece stays the hull and keeps the snapshot; every other piece links back to that hull.</summary>
    private void OnGridSplit(ref GridSplitEvent args)
    {
        var hull = HasComp<ShipRepairDataComponent>(args.Grid)
            ? args.Grid
            : CompOrNull<WFHullSectionComponent>(args.Grid)?.Hull;

        if (hull is not { } owner || TerminatingOrDeleted(owner))
            return;

        foreach (var grid in args.NewGrids)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            var section = EnsureComp<WFHullSectionComponent>(grid);
            section.Hull = owner;
            Dirty(grid, section);
        }
    }

    // A deleted hull is detached from its map before its components go, so it hands on while it still has one.
    private void OnHullTerminating(Entity<ShipRepairDataComponent> ent, ref EntityTerminatingEvent args)
    {
        HandOn(ent);
    }

    private void OnSnapshotRemoved(Entity<ShipRepairDataComponent> ent, ref ComponentRemove args)
    {
        if (!TerminatingOrDeleted(ent))
            HandOn(ent);
    }

    /// <summary>A hull losing its snapshot hands it to its largest section on the same map; without one, its sections are wreckage.</summary>
    private void HandOn(Entity<ShipRepairDataComponent> ent)
    {
        var map = Transform(ent).MapUid;
        var mapGone = map == null || TerminatingOrDeleted(map.Value);
        var sections = new List<Entity<WFHullSectionComponent>>();
        Entity<MapGridComponent>? heir = null;
        var heirTiles = 0;

        var query = EntityQueryEnumerator<WFHullSectionComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var section, out var grid, out var xform))
        {
            if (section.Hull != ent.Owner || TerminatingOrDeleted(uid))
                continue;

            sections.Add((uid, section));
            if (mapGone || xform.MapUid != map)
                continue;

            var tiles = CountTiles((uid, grid));
            if (tiles <= heirTiles)
                continue;

            heir = (uid, grid);
            heirTiles = tiles;
        }

        if (heir is { } next)
        {
            CopySnapshot(ent.Comp, next);
            if (TryComp<ShipRepairRestrictComponent>(ent, out var restrict))
                EnsureComp<ShipRepairRestrictComponent>(next).ToolWhitelist = restrict.ToolWhitelist;
        }

        foreach (var section in sections)
        {
            if (heir is not { } successor || section.Owner == successor.Owner)
            {
                RemComp<WFHullSectionComponent>(section);
                continue;
            }

            section.Comp.Hull = successor.Owner;
            Dirty(section);
        }
    }

    /// <summary>How many tiles a grid has.</summary>
    private int CountTiles(Entity<MapGridComponent> grid)
    {
        var count = 0;
        var tiles = _map.GetAllTilesEnumerator(grid, grid.Comp);
        while (tiles.MoveNext(out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>Gives a grid its own copy of a repair snapshot.</summary>
    private void CopySnapshot(ShipRepairDataComponent source, EntityUid target)
    {
        var copy = EnsureComp<ShipRepairDataComponent>(target);
        copy.ChunkSize = source.ChunkSize;
        copy.EntityPalette = new List<EntProtoId>(source.EntityPalette);
        copy.Chunks.Clear();

        foreach (var (index, chunk) in source.Chunks)
        {
            var chunkCopy = new ShipRepairChunk
            {
                Tiles = (int[]) chunk.Tiles.Clone(),
                NextUid = chunk.NextUid,
            };

            foreach (var (id, spec) in chunk.Entities)
            {
                chunkCopy.Entities[id] = new ShipRepairEntitySpecifier
                {
                    ProtoIndex = spec.ProtoIndex,
                    LocalPosition = spec.LocalPosition,
                    Rotation = spec.Rotation,
                    OriginalEntity = spec.OriginalEntity,
                };
            }

            copy.Chunks[index] = chunkCopy;
        }

        Dirty(target, copy);
    }

}
