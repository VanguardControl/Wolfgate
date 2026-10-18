using System.Linq;
using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.PlanetPois;
using Content.Shared.EntityTable;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared.Physics;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

/// <summary>
/// Crashed ships: a shipyard hull, torn open where it hit, lying at whatever angle it came to rest off the roads, with
/// the world creeping over its deck and its wildlife living inside.
/// </summary>
public sealed partial class WFPlanetPoiSystem
{
    [Dependency] private EntityTableSystem _entityTables = default!;

    private const string WreckName = "wf-poi-wreck";
    private const int WreckAttempts = 64;

    /// <summary>Tiles kept between a wreck and anything pinned: sites, roads, cavern mouths.</summary>
    private const float WreckClearance = 3f;

    private void PlaceWrecks(EntityUid ground, MapGridComponent grid, BiomeComponent biome, EntityUid orbit, Vector2 centre, float radius,
        WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record, HashSet<EntityUid> created)
    {
        if (table.Wrecks is not { } wrecks)
            return;
        if (!_proto.TryIndex(wrecks.Theme, out var theme))
        {
            Log.Error($"Unknown wreck theme {wrecks.Theme} in {table.ID}.");
            return;
        }

        var pool = _proto.EnumeratePrototypes<VesselPrototype>()
            .Where(vessel => !vessel.Abstract && vessel.Purchasable && wrecks.Sizes.Contains(vessel.Category) && vessel.ShuttlePath != default)
            .OrderBy(vessel => vessel.ID)
            .ToList();
        if (pool.Count == 0)
            return;

        var count = _random.Next(wrecks.MinCount, wrecks.MaxCount + 1);
        for (var i = 0; i < count; i++)
        {
            if (TerminatingOrDeleted(ground))
                return;
            var vessel = _random.Pick(pool);
            if (!TryPlaceWreck(ground, grid, biome, orbit, centre, radius, table, record, wrecks, theme, vessel, created))
                Log.Warning($"No wreck of {vessel.ID} on {ToPrettyString(ground)}; it is skipped.");
        }
    }

    private bool TryPlaceWreck(EntityUid ground, MapGridComponent grid, BiomeComponent biome, EntityUid orbit, Vector2 centre, float radius,
        WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record, WFPlanetWrecks wrecks, WFWreckThemePrototype theme, VesselPrototype vessel,
        HashSet<EntityUid> created)
    {
        var stagingMap = _maps.CreateMap(out var stagingId, runMapInit: false);
        try
        {
            if (!_loader.TryLoadGrid(stagingId, vessel.ShuttlePath, out var loaded))
                return false;
            var ship = loaded.Value;
            var hull = ship.Comp.LocalAABB;
            var reach = hull.Size.Length() / 2f;
            if (!TryWreckSpot((ground, biome), grid, centre, radius, table, reach, out var spot))
                return false;

            WreckHull(ship, theme, wrecks.Breach);

            // The hull comes to rest at any angle, its middle on the spot.
            var angle = _random.NextAngle();
            var targetMap = Transform(ground).MapUid ?? ground;
            var world = _transform.ToMapCoordinates(new EntityCoordinates(ground, spot)).Position - angle.RotateVec(hull.Center);
            _transform.SetCoordinates(ship.Owner, new EntityCoordinates(targetMap, world));
            _transform.SetLocalRotation(ship.Owner, angle);
            created.Add(ship.Owner);
            InitializeTransferred(ship.Owner, _maps.IsInitialized(targetMap), _maps.IsPaused(targetMap));
            SettleWreck(ship);

            // Clear and pin the ground under it, so nothing the biome grows comes up through the deck.
            var under = new HashSet<Vector2i>();
            var anchored = new List<EntityUid>();
            for (var x = (int) MathF.Floor(spot.X - reach); x <= (int) MathF.Ceiling(spot.X + reach); x++)
            for (var y = (int) MathF.Floor(spot.Y - reach); y <= (int) MathF.Ceiling(spot.Y + reach); y++)
            {
                var tile = new Vector2i(x, y);
                if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), spot) > reach)
                    continue;
                under.Add(tile);
                anchored.Clear();
                var enumerator = _maps.GetAnchoredEntitiesEnumerator(ground, grid, tile);
                while (enumerator.MoveNext(out var uid))
                    anchored.Add(uid.Value);
                foreach (var uid in anchored)
                    QueueDel(uid);
            }

            // Ground the biome hasn't laid yet would stay empty once pinned: lay it now.
            var fill = new List<(Vector2i, Tile)>();
            foreach (var tile in under)
            {
                if (_maps.GetTileRef(ground, grid, tile).Tile.IsEmpty && _biome.TryGetBiomeTile(ground, grid, tile, out var natural))
                    fill.Add((tile, natural.Value));
            }
            if (fill.Count > 0)
                _maps.SetTiles(ground, grid, fill);
            _biome.WfPinTiles((ground, biome), under);

            SpawnWreckFauna(ship, theme);

            var origin = spot.Floored();
            var bounds = new Box2i((int) MathF.Floor(spot.X - reach), (int) MathF.Floor(spot.Y - reach),
                (int) MathF.Ceiling(spot.X + reach) + 1, (int) MathF.Ceiling(spot.Y + reach) + 1);
            var site = new WFPlanetPoiSite
            {
                Name = WreckName,
                Origin = origin,
                Bounds = bounds,
                TileCount = under.Count,
                Connection = WFPlanetPoiConnection.Isolated,
                Expeditions = false,
                Difficulty = table.Difficulty,
            };
            site.Signal = SpawnSignal(ground, orbit, origin, new WFPlanetPoiEntry { Name = WreckName }, table);
            created.Add(site.Signal);
            record.Sites.Add(site);
            return true;
        }
        finally
        {
            if (!TerminatingOrDeleted(stagingMap))
                QueueDel(stagingMap);
        }
    }

    /// <summary>Somewhere off the roads, clear of the sites and the landing zone, with nothing pinned under the hull.</summary>
    private bool TryWreckSpot(Entity<BiomeComponent> ground, MapGridComponent grid, Vector2 centre, float radius, WFPlanetPoiTablePrototype table,
        float reach, out Vector2 spot)
    {
        spot = default;
        var inner = table.CentreClear + reach + WreckClearance;
        var outer = radius - table.EdgeMargin - reach;
        if (outer <= inner)
            return false;

        var clear = reach + WreckClearance;
        for (var attempt = 0; attempt < WreckAttempts; attempt++)
        {
            spot = centre + _random.NextAngle().ToVec() * MathF.Sqrt(_random.NextFloat(inner * inner, outer * outer));
            var free = true;
            for (var x = (int) MathF.Floor(spot.X - clear); x <= (int) MathF.Ceiling(spot.X + clear) && free; x++)
            for (var y = (int) MathF.Floor(spot.Y - clear); y <= (int) MathF.Ceiling(spot.Y + clear) && free; y++)
            {
                if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), spot) <= clear && _biome.WfIsPinned(ground, new Vector2i(x, y)))
                    free = false;
            }
            if (free)
                return true;
        }
        return false;
    }

    /// <summary>Tears the hull open where it hit, lets the world creep over the deck and grow on it.</summary>
    private void WreckHull(Entity<MapGridComponent> ship, WFWreckThemePrototype theme, float breach)
    {
        ship.Comp.CanSplit = false;
        var tiles = _maps.GetAllTiles(ship.Owner, ship.Comp).Select(t => t.GridIndices).ToHashSet();
        if (tiles.Count == 0)
            return;

        // The breach: a ragged bite out of the hull's edge.
        var edge = tiles.Where(t => !tiles.Contains(t + Vector2i.Up) || !tiles.Contains(t + Vector2i.Down)
                                     || !tiles.Contains(t + Vector2i.Left) || !tiles.Contains(t + Vector2i.Right)).ToList();
        var hull = ship.Comp.LocalAABB;
        var bite = MathF.Max(2f, breach * MathF.Min(hull.Width, hull.Height));
        var impact = (Vector2) _random.Pick(edge) + new Vector2(0.5f);
        var torn = tiles.Where(t =>
        {
            var distance = Vector2.Distance((Vector2) t + new Vector2(0.5f), impact);
            return distance <= bite || distance <= bite + 1.5f && _random.Prob(0.5f);
        }).ToHashSet();

        var children = new List<EntityUid>();
        var enumerator = Transform(ship.Owner).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
            children.Add(child);
        foreach (var child in children)
        {
            if (torn.Contains(Transform(child).LocalPosition.Floored()) || HasComp<ShuttleConsoleComponent>(child))
                Del(child);
        }
        _maps.SetTiles(ship.Owner, ship.Comp, torn.Select(t => (t, Tile.Empty)).ToList());
        tiles.ExceptWith(torn);

        var occupied = SolidTiles(ship);

        if (theme.FloorTile is { } floorId && _tiles.TryGetDefinition(floorId, out var floor))
        {
            var creep = tiles.Where(t => !occupied.Contains(t) && _random.Prob(theme.FloorChance)).Select(t => (t, new Tile(floor.TileId))).ToList();
            _maps.SetTiles(ship.Owner, ship.Comp, creep);
        }

        if (theme.Overgrowth.Count > 0)
        {
            foreach (var tile in tiles)
            {
                if (occupied.Contains(tile) || !_random.Prob(theme.OvergrowthChance))
                    continue;
                Spawn(_random.Pick(theme.Overgrowth), new EntityCoordinates(ship.Owner, (Vector2) tile + new Vector2(0.5f)));
            }
        }
    }

    /// <summary>A wreck is ground now: it doesn't fly, fall, belong to anyone or count as a vessel, and it has gravity and a roof.</summary>
    private void SettleWreck(Entity<MapGridComponent> ship)
    {
        RemComp<ShuttleComponent>(ship);
        RemComp<ShuttleDeedComponent>(ship);
        RemComp<BecomesStationComponent>(ship);
        EnsureComp<CEZMappingAnchorGridComponent>(ship);
        EnsureComp<WFPlanetCityGridComponent>(ship);
        _physics.SetBodyType(ship, BodyType.Static);

        var gravity = EnsureComp<GravityComponent>(ship);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(ship, gravity);

        EnsureComp<RoofComponent>(ship);
        foreach (var tile in _maps.GetAllTiles(ship.Owner, ship.Comp))
            _roof.SetRoof((ship.Owner, ship.Comp, null), tile.GridIndices, true);
    }

    /// <summary>Deck under walls and solid machines; cables and pipes under the deck don't count.</summary>
    private HashSet<Vector2i> SolidTiles(Entity<MapGridComponent> ship)
    {
        var solid = new HashSet<Vector2i>();
        var enumerator = Transform(ship.Owner).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            if (Transform(child).Anchored && TryComp(child, out FixturesComponent? fixtures)
                && fixtures.Fixtures.Values.Any(f => f.Hard && (f.CollisionLayer & (int) CollisionGroup.MidImpassable) != 0))
                solid.Add(Transform(child).LocalPosition.Floored());
        }
        return solid;
    }

    private void SpawnWreckFauna(Entity<MapGridComponent> ship, WFWreckThemePrototype theme)
    {
        if (theme.Fauna is not { } fauna)
            return;
        var solid = SolidTiles(ship);
        var open = _maps.GetAllTiles(ship.Owner, ship.Comp).Select(t => t.GridIndices).Where(t => !solid.Contains(t)).ToList();
        if (open.Count == 0)
            return;

        var count = _random.Next(theme.MinFauna, theme.MaxFauna + 1);
        for (var i = 0; i < count; i++)
        {
            foreach (var id in _entityTables.GetSpawns(fauna))
                Spawn(id, new EntityCoordinates(ship.Owner, (Vector2) _random.Pick(open) + new Vector2(0.5f)));
        }
    }
}
