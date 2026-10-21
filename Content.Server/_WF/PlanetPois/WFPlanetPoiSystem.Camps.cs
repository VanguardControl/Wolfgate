using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.PlanetPois;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

/// <summary>The hull an armed camp is built round.</summary>
[RegisterComponent]
public sealed partial class WFPlanetCampComponent : Component
{
    [ViewVariables]
    public ProtoId<WFPlanetCampPrototype> Camp;
}

/// <summary>
/// Armed camps: a ship set down intact off the roads, its crew standing guard on the deck and round the hull behind a
/// ring of cover. The deck guards come aboard with the ship; the ones outside, and their cover, wait until the ground
/// round it exists.
/// </summary>
public sealed partial class WFPlanetPoiSystem
{
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private NpcFactionSystem _factions = default!;

    private int _campCount;

    /// <summary>Tiles of camp round a hull: guards, cover, tents and fires, all on cleared ground.</summary>
    private const float CampRing = 8f;

    private void PlaceCamps(EntityUid ground, MapGridComponent grid, BiomeComponent biome, EntityUid orbit, Vector2 centre, float radius,
        WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record, HashSet<EntityUid> created)
    {
        foreach (var entry in table.Camps)
        {
            if (!_proto.TryIndex(entry.Camp, out var camp))
            {
                Log.Error($"Unknown camp {entry.Camp} in {table.ID}.");
                continue;
            }

            var count = _random.Next(entry.MinCount, entry.MaxCount + 1);
            for (var i = 0; i < count; i++)
            {
                if (TerminatingOrDeleted(ground))
                    return;
                if (!TryPlaceCamp(ground, grid, biome, orbit, centre, radius, table, record, camp, created))
                    Log.Warning($"No {camp.ID} on {ToPrettyString(ground)}; it is skipped.");
            }
        }
    }

    private bool TryPlaceCamp(EntityUid ground, MapGridComponent grid, BiomeComponent biome, EntityUid orbit, Vector2 centre, float radius,
        WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record, WFPlanetCampPrototype camp, HashSet<EntityUid> created)
    {
        if (camp.Vessels.Count == 0 || !_proto.TryIndex(_random.Pick(camp.Vessels), out var vessel))
            return false;

        var stagingMap = _maps.CreateMap(out var stagingId, runMapInit: false);
        try
        {
            if (!_loader.TryLoadGrid(stagingId, vessel.ShuttlePath, out var loaded))
                return false;
            var ship = loaded.Value;
            var hull = ship.Comp.LocalAABB;
            // Room round the hull for the guards and their cover.
            var reach = hull.Size.Length() / 2f;
            var camped = reach + CampRing;
            if (!TryWreckSpot((ground, biome), grid, centre, radius, table, camped, out var spot))
                return false;

            // Set down square to the world, the right way up.
            var angle = Angle.FromDegrees(90 * _random.Next(4));
            var targetMap = Transform(ground).MapUid ?? ground;
            var world = _transform.ToMapCoordinates(new EntityCoordinates(ground, spot)).Position - angle.RotateVec(hull.Center);
            _transform.SetCoordinates(ship.Owner, new EntityCoordinates(targetMap, world));
            _transform.SetLocalRotation(ship.Owner, angle);
            ship.Comp.CanSplit = false;
            created.Add(ship.Owner);
            InitializeTransferred(ship.Owner, _maps.IsInitialized(targetMap), _maps.IsPaused(targetMap));
            SettleWreck(ship);
            EnsureComp<WFPlanetCampComponent>(ship).Camp = camp.ID;

            // The ground under the hull and the camp round it: cleared, laid and pinned.
            var under = new HashSet<Vector2i>();
            var anchored = new List<EntityUid>();
            for (var x = (int) MathF.Floor(spot.X - camped); x <= (int) MathF.Ceiling(spot.X + camped); x++)
            for (var y = (int) MathF.Floor(spot.Y - camped); y <= (int) MathF.Ceiling(spot.Y + camped); y++)
            {
                var tile = new Vector2i(x, y);
                if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), spot) > camped)
                    continue;
                under.Add(tile);
                anchored.Clear();
                var enumerator = _maps.GetAnchoredEntitiesEnumerator(ground, grid, tile);
                while (enumerator.MoveNext(out var uid))
                    anchored.Add(uid.Value);
                foreach (var uid in anchored)
                    QueueDel(uid);
            }
            var fill = new List<(Vector2i, Tile)>();
            foreach (var tile in under)
            {
                if (_maps.GetTileRef(ground, grid, tile).Tile.IsEmpty && _biome.TryGetBiomeTile(ground, grid, tile, out var natural))
                    fill.Add((tile, natural.Value));
            }
            if (fill.Count > 0)
                _maps.SetTiles(ground, grid, fill);
            _biome.WfPinTiles((ground, biome), under);

            var group = $"{camp.ID}-{++_campCount}";
            var deck = _maps.GetAllTiles(ship.Owner, ship.Comp).Select(t => t.GridIndices).Except(SolidTiles(ship)).ToList();
            if (deck.Count > 0)
            {
                var guards = _random.Next(camp.MinDeckGuards, camp.MaxDeckGuards + 1);
                for (var i = 0; i < guards; i++)
                    SpawnCampGuard(camp, new EntityCoordinates(ship.Owner, (Vector2) _random.Pick(deck) + new Vector2(0.5f)), group);

                if (camp.Loot is { } loot)
                {
                    foreach (var id in _entityTables.GetSpawns(loot))
                        created.Add(Spawn(id, new EntityCoordinates(ship.Owner, (Vector2) _random.Pick(deck) + new Vector2(0.5f))));
                }
            }

            record.PendingCamps.Add(new WFPendingCamp(camp.ID, spot, reach, group));

            var origin = spot.Floored();
            var bounds = new Box2i((int) MathF.Floor(spot.X - camped), (int) MathF.Floor(spot.Y - camped),
                (int) MathF.Ceiling(spot.X + camped) + 1, (int) MathF.Ceiling(spot.Y + camped) + 1);
            var site = new WFPlanetPoiSite
            {
                Name = camp.Name,
                Origin = origin,
                Bounds = bounds,
                TileCount = under.Count,
                Connection = WFPlanetPoiConnection.Isolated,
                Expeditions = false,
                Difficulty = table.Difficulty,
            };
            site.Signal = SpawnSignal(ground, orbit, origin, new WFPlanetPoiEntry { Name = camp.Name }, table);
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

    /// <summary>Once the ground exists: the guards outside, in a loose ring round the hull, and their cover.</summary>
    private void SpawnPendingCamps(Entity<BiomeComponent> ground, MapGridComponent grid, WFPlanetPoiComponent record)
    {
        var camps = record.PendingCamps.ToList();
        record.PendingCamps.Clear();
        foreach (var pending in camps)
        {
            if (!_proto.TryIndex(pending.Camp, out var camp))
                continue;

            bool Open(Vector2i tile) => !_maps.GetTileRef(ground, grid, tile).Tile.IsEmpty
                                        && !_maps.GetAnchoredEntitiesEnumerator(ground, grid, tile).MoveNext(out _);

            Vector2i? RingTile(float distance)
            {
                for (var attempt = 0; attempt < 24; attempt++)
                {
                    var tile = (pending.Centre + _random.NextAngle().ToVec() * distance).Floored();
                    if (Open(tile))
                        return tile;
                }
                return null;
            }

            foreach (var prop in camp.Props)
            {
                if (prop.Ids.Count == 0)
                    continue;
                var props = _random.Next(prop.Min, prop.Max + 1);
                for (var i = 0; i < props; i++)
                {
                    if (RingTile(pending.Reach + MathF.Min(prop.Distance, CampRing - 1f)) is { } tile)
                        Spawn(_random.Pick(prop.Ids), new EntityCoordinates(ground, (Vector2) tile + new Vector2(0.5f)));
                }
            }

            var cover = camp.Cover.Count == 0 ? 0 : _random.Next(camp.MinCover, camp.MaxCover + 1);
            for (var i = 0; i < cover; i++)
            {
                if (RingTile(pending.Reach + 3f) is { } tile)
                    Spawn(_random.Pick(camp.Cover), new EntityCoordinates(ground, (Vector2) tile + new Vector2(0.5f)));
            }

            var guards = _random.Next(camp.MinGroundGuards, camp.MaxGroundGuards + 1);
            for (var i = 0; i < guards; i++)
            {
                if (RingTile(pending.Reach + _random.NextFloat(1.5f, 2.5f)) is { } tile)
                    SpawnCampGuard(camp, new EntityCoordinates(ground, (Vector2) tile + new Vector2(0.5f)), pending.Group);
            }
        }
    }

    /// <summary>One guard from the camp's profile, posted where he stands, on the camp's side.</summary>
    private EntityUid? SpawnCampGuard(WFPlanetCampPrototype camp, EntityCoordinates post, string group)
    {
        _proto.TryIndex(camp.Profile, out var profile);
        ProtoId<StartingGearPrototype>? loadout = null;
        if (profile != null && profile.Loadouts.TryGetValue(camp.Role, out var pool) && pool.Count > 0)
            loadout = _random.Pick(pool);
        var body = loadout != null && profile != null && profile.Bodies.Count > 0 ? _random.Pick(profile.Bodies) : (EntProtoId?) null;
        string? name = null;
        if (profile?.FirstNames is { } firstId && profile.LastNames is { } lastId
            && _proto.TryIndex(firstId, out var first) && _proto.TryIndex(lastId, out var last)
            && first.Values.Count > 0 && last.Values.Count > 0)
            name = $"{_random.Pick(first.Values)} {_random.Pick(last.Values)}";

        if (_crew.SpawnCrewman(camp.Role, post, group, loadout, body, name, camp.Profile) is not { } guard)
            return null;

        if (profile != null && profile.Components.Count > 0)
            EntityManager.AddComponents(guard, profile.Components);
        if (TryComp<WFCrewComponent>(guard, out var crewman))
        {
            crewman.Skill = camp.Skill;
            _crew.Apply((guard, crewman));
        }

        EnsureComp<NpcFactionMemberComponent>(guard);
        _factions.ClearFactions(guard, false);
        _factions.AddFaction(guard, camp.Faction);
        return guard;
    }
}
