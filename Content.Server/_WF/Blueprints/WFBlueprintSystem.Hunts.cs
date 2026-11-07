using System.Linq;
using Content.Server._WF.PlanetPois;
using Content.Server.Storage.Components;
using Content.Shared._WF.Blueprints;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Salvage;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Blueprints;

public sealed partial class WFBlueprintSystem
{
    public static readonly EntProtoId ShardProto = "WFBlueprintShard";

    private void OnLocate(Entity<WFBlueprintLocatorComponent> ent, ref WFBlueprintLocateMessage args)
    {
        var user = args.Actor;
        if (TryLocate(ent, user, args.Patterns, out var error))
            return;

        _popup.PopupEntity(Loc.GetString(error), ent, user, PopupType.MediumCaution);
    }

    /// <summary>Validates a request, takes the fee, claims a site and hides the shards; error is a loc id.</summary>
    public bool TryLocate(Entity<WFBlueprintLocatorComponent> ent, EntityUid user, List<string> ids, out string error)
    {
        error = string.Empty;
        if (ent.Comp.Hunt != null)
        {
            error = "wf-blueprint-locator-busy";
            return false;
        }

        if (_timing.CurTime < ent.Comp.NextHunt)
        {
            error = "wf-blueprint-locator-cooldown";
            return false;
        }

        if (!TryGetDatabase(ent, out _, out _))
        {
            error = "wf-blueprint-locator-unlinked";
            return false;
        }

        var patterns = new List<WFBlueprintPatternPrototype>();
        foreach (var id in ids.Distinct())
        {
            if (!_proto.TryIndex<WFBlueprintPatternPrototype>(id, out var pattern) || IsOwned(ent, pattern))
            {
                error = "wf-blueprint-locator-invalid";
                return false;
            }

            patterns.Add(pattern);
        }

        if (patterns.Count == 0 || patterns.Count > ent.Comp.MaxPatterns || patterns.Any(p => p.Tier != patterns[0].Tier))
        {
            error = "wf-blueprint-locator-invalid";
            return false;
        }

        var tier = patterns[0].Tier;
        if (!TryPickSite(tier, out var ground, out var index))
        {
            error = "wf-blueprint-locator-no-site";
            return false;
        }

        var fee = ent.Comp.Fees[Math.Clamp(tier - 1, 0, ent.Comp.Fees.Length - 1)];
        if (fee > 0 && !_bank.TryBankWithdraw(user, fee))
        {
            error = "wf-blueprint-locator-no-funds";
            return false;
        }

        var site = Comp<WFPlanetPoiComponent>(ground).Sites[index];
        var hunt = new WFBlueprintHunt
        {
            Ground = ground,
            SiteIndex = index,
            Tier = tier,
            Patterns = patterns.Select(p => new ProtoId<WFBlueprintPatternPrototype>(p.ID)).ToList(),
        };

        site.BlueprintClaim = ent;
        ent.Comp.Hunt = hunt;
        PlaceShards(ent, hunt, site, patterns);

        if (TryComp<WFPoiSignalComponent>(site.Signal, out var signal) && !signal.Revealed)
            _pois.Reveal((site.Signal, signal), null);

        _popup.PopupEntity(Loc.GetString("wf-blueprint-locator-started",
            ("site", Loc.GetString(site.Name)), ("world", Name(ground))), ent, user);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} started a tier {tier} blueprint hunt at {ToPrettyString(ent)} for {string.Join(", ", hunt.Patterns)}");
        UpdateUi(ent);
        return true;
    }

    /// <summary>Picks a free planet site whose rating suits the tier.</summary>
    private bool TryPickSite(int tier, out EntityUid ground, out int index)
    {
        var free = new List<(EntityUid Ground, int Index)>();
        var query = EntityQueryEnumerator<WFPlanetPoiComponent>();
        while (query.MoveNext(out var uid, out var record))
        {
            if (!record.NetworkReady)
                continue;

            for (var i = 0; i < record.Sites.Count; i++)
            {
                var site = record.Sites[i];
                if (site.BlueprintClaim == null && site.ExpeditionClaim == null && TierOf(site.Difficulty) == tier)
                    free.Add((uid, i));
            }
        }

        ground = default;
        index = -1;
        if (free.Count == 0)
            return false;

        (ground, index) = _random.Pick(free);
        return true;
    }

    /// <summary>The hunt tier a site's rating belongs to.</summary>
    public static int TierOf(DifficultyRating rating)
    {
        return rating switch
        {
            DifficultyRating.Extreme => 3,
            DifficultyRating.Hazardous => 2,
            _ => 1,
        };
    }

    /// <summary>One shard a pattern: inside the site's crates and lockers first, then on its open floor.</summary>
    private void PlaceShards(Entity<WFBlueprintLocatorComponent> ent, WFBlueprintHunt hunt, WFPlanetPoiSite site, List<WFBlueprintPatternPrototype> patterns)
    {
        if (!TryComp<MapGridComponent>(hunt.Ground, out var grid))
            return;

        bool InSite(Vector2i tile) => tile.X >= site.Bounds.Left && tile.X < site.Bounds.Right
            && tile.Y >= site.Bounds.Bottom && tile.Y < site.Bounds.Top;

        var storages = new List<EntityUid>();
        var query = EntityQueryEnumerator<EntityStorageComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.ParentUid == hunt.Ground && !HasComp<MobStateComponent>(uid)
                && InSite(_map.LocalToTile(hunt.Ground, grid, xform.Coordinates)))
                storages.Add(uid);
        }

        var floor = site.Tiles.Where(tile => InSite(tile)
            && !_map.GetTileRef(hunt.Ground, grid, tile).Tile.IsEmpty
            && !_turf.IsTileBlocked(hunt.Ground, tile, CollisionGroup.Impassable, grid)).ToList();

        _random.Shuffle(storages);
        _random.Shuffle(floor);

        var nextStorage = 0;
        var nextFloor = 0;
        foreach (var pattern in patterns)
        {
            EntityUid shard;
            if (nextStorage < storages.Count)
            {
                var storage = storages[nextStorage++];
                shard = SpawnFor(ShardProto, pattern, Transform(storage).Coordinates, "wf-blueprint-shard-name");
                _entityStorage.Insert(shard, storage);
            }
            else if (nextFloor < floor.Count)
            {
                shard = SpawnFor(ShardProto, pattern, _map.GridTileToLocal(hunt.Ground, grid, floor[nextFloor++]), "wf-blueprint-shard-name");
            }
            else
            {
                // A site with no free floor: the shard lies at its middle.
                var centre = new Vector2i((site.Bounds.Left + site.Bounds.Right) / 2, (site.Bounds.Bottom + site.Bounds.Top) / 2);
                shard = SpawnFor(ShardProto, pattern, _map.GridTileToLocal(hunt.Ground, grid, centre), "wf-blueprint-shard-name");
            }

            Comp<WFBlueprintShardComponent>(shard).Locator = ent;
            hunt.Shards.Add(shard);
        }
    }

    private void OnShardShutdown(Entity<WFBlueprintShardComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Locator is not { } locator || !TryComp<WFBlueprintLocatorComponent>(locator, out var comp)
            || comp.Hunt is not { } hunt)
            return;

        hunt.Shards.Remove(ent);
        if (hunt.Shards.Count == 0)
            EndHunt((locator, comp));
        else
            UpdateUi((locator, comp));
    }

    private void OnAbandon(Entity<WFBlueprintLocatorComponent> ent, ref WFBlueprintAbandonMessage args)
    {
        if (ent.Comp.Hunt == null)
            return;

        Abandon(ent);
        _popup.PopupEntity(Loc.GetString("wf-blueprint-locator-abandoned"), ent, args.Actor);
    }

    /// <summary>Ends the hunt early: shards still at the site go, shards someone carries stay theirs.</summary>
    public void Abandon(Entity<WFBlueprintLocatorComponent> ent)
    {
        if (ent.Comp.Hunt is not { } hunt)
            return;

        foreach (var uid in hunt.Shards.ToList())
        {
            if (!TryComp<WFBlueprintShardComponent>(uid, out var shard))
                continue;

            shard.Locator = null;
            if (!IsCarried(uid))
                QueueDel(uid);
        }

        hunt.Shards.Clear();
        EndHunt(ent);
    }

    private bool IsCarried(EntityUid uid)
    {
        var current = uid;
        while (_container.TryGetContainingContainer(current, out var container))
        {
            if (HasComp<MobStateComponent>(container.Owner))
                return true;

            current = container.Owner;
        }

        return false;
    }

    /// <summary>Releases the site and starts the cooldown.</summary>
    private void EndHunt(Entity<WFBlueprintLocatorComponent> ent)
    {
        if (ent.Comp.Hunt is not { } hunt)
            return;

        if (TryComp<WFPlanetPoiComponent>(hunt.Ground, out var record) && hunt.SiteIndex < record.Sites.Count
            && record.Sites[hunt.SiteIndex].BlueprintClaim == ent.Owner)
            record.Sites[hunt.SiteIndex].BlueprintClaim = null;

        ent.Comp.Hunt = null;
        ent.Comp.NextHunt = _timing.CurTime + ent.Comp.Cooldown;
        UpdateUi(ent);
    }

    private void OnLocatorShutdown(Entity<WFBlueprintLocatorComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Hunt is not { } hunt)
            return;

        foreach (var uid in hunt.Shards)
        {
            if (TryComp<WFBlueprintShardComponent>(uid, out var shard))
                shard.Locator = null;
        }

        if (TryComp<WFPlanetPoiComponent>(hunt.Ground, out var record) && hunt.SiteIndex < record.Sites.Count)
            record.Sites[hunt.SiteIndex].BlueprintClaim = null;

        ent.Comp.Hunt = null;
    }
}
