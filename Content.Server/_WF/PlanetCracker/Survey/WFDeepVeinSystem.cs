using Content.Server._WF.PlanetCracker.Planets;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Mining;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetCracker.Survey;

/// <summary>Filters a marker-spawned deep vein by tile, then rolls its ore and yield from seed and tile.</summary>
public sealed partial class WFDeepVeinSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // A different pair from the shared system's <WFDeepVeinComponent, ExaminedEvent>.
        SubscribeLocalEvent<WFDeepVeinComponent, MapInitEvent>(OnMapInit);
    }

    /// <summary>Filters the vein onto ground it belongs on, then rolls its contents once and for good.</summary>
    private void OnMapInit(Entity<WFDeepVeinComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent.Owner);

        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
        {
            QueueDel(ent.Owner);
            return;
        }

        var idx = _map.TileIndicesFor(grid, mapGrid, xform.Coordinates);

        if (!_map.TryGetTile(mapGrid, idx, out var tile))
        {
            QueueDel(ent.Owner);
            return;
        }

        // Biome marker layers have no tile whitelist, so veins are filtered here.
        if (!IsAllowedTile(ent.Comp, _tileDefs[tile.TypeId].ID))
        {
            QueueDel(ent.Owner);
            return;
        }

        if (!TryGetVeinTable(grid, out var table, out var sanctioned))
        {
            QueueDel(ent.Owner);
            return;
        }

        // Deterministic; never HashCode.Combine or string.GetHashCode, which are randomised per process.
        var seed = TryComp<BiomeComponent>(grid, out var biome) ? biome.Seed : 0;
        var mix = unchecked(seed * 397 ^ idx.X * 73856093 ^ idx.Y * 19349663);
        var rand = new Random(mix);

        ent.Comp.Ore = PickOre(table, rand);

        // The multiplier scales the whole band, so "rich" is measured against the scaled range.
        var multiplier = sanctioned ? 1f : table.UnsanctionedMultiplier;
        var rolled = rand.Next((int) table.YieldRange.X, (int) table.YieldRange.Y + 1);
        ent.Comp.YieldRange = table.YieldRange * multiplier;
        ent.Comp.TotalYield = (int) (rolled * multiplier);
        ent.Comp.Rich = ent.Comp.TotalYield >= ent.Comp.YieldRange.Y * 0.75f;
        ent.Comp.Rate = table.Rate;
        ent.Comp.Remaining = ent.Comp.TotalYield;
        Dirty(ent.Owner, ent.Comp);

        // The biome never anchors markers; the prototype's `anchored: true` lets the vein ride the chunk.
        if (!xform.Anchored)
            Log.Error($"Deep vein {ToPrettyString(ent.Owner)} spawned unanchored; its prototype is missing `anchored: true`.");
    }

    /// <summary>Whether the vein's whitelist admits the tile it landed on.</summary>
    private static bool IsAllowedTile(WFDeepVeinComponent comp, string tileId)
    {
        foreach (var allowed in comp.AllowedTiles)
        {
            if (allowed.Id == tileId)
                return true;
        }

        return false;
    }

    /// <summary>The vein table of the world this grid is the ground of, and whether cracking that world is legal.</summary>
    private bool TryGetVeinTable(EntityUid grid, out WFVeinTablePrototype table, out bool sanctioned)
    {
        table = default!;
        sanctioned = true;

        if (!TryComp<WFPlanetLayerComponent>(grid, out var layer))
            return false;

        if (layer.Network is not { } network || !TryGetEntity(network, out var networkUid))
            return false;

        if (!TryComp<WFPlanetNetworkComponent>(networkUid, out var networkComp))
            return false;

        if (!_proto.TryIndex(networkComp.Surface, out var surface))
            return false;

        sanctioned = surface.Sanctioned;

        return surface.Veins is { } tableId && _proto.TryIndex(tableId, out table!);
    }

    /// <summary>One weighted draw over the table's ores; entries with no weight never come up.</summary>
    private static ProtoId<OrePrototype> PickOre(WFVeinTablePrototype table, Random rand)
    {
        var totalWeight = 0f;

        foreach (var entry in table.Ores.Values)
        {
            if (entry.Weight > 0f)
                totalWeight += entry.Weight;
        }

        var picked = default(ProtoId<OrePrototype>);

        if (totalWeight <= 0f)
            return picked;

        var roll = rand.NextDouble() * totalWeight;

        foreach (var (ore, entry) in table.Ores)
        {
            if (entry.Weight <= 0f)
                continue;

            picked = ore;
            roll -= entry.Weight;

            if (roll <= 0d)
                break;
        }

        return picked;
    }
}
