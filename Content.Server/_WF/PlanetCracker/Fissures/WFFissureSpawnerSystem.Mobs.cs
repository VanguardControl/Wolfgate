using System.Numerics;
using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WF.PlanetCracker.Fissures;
using Content.Shared.Mobs.Components;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Storage;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetCracker.Fissures;

/// <summary>What climbs out of a fissure: the cumulative cap, the faction roll and the site-threat stamp.</summary>
public sealed partial class WFFissureSpawnerSystem
{
    /// <summary>The HTN compound a stamped threat is re-rooted on, so it goes for the anchor before it goes for people.</summary>
    private const string ThreatCompound = "WFFissureThreatCompound";

    /// <summary>One mob per fissure up to the roll, the ring's own tile count and whatever is left of the cap.</summary>
    private void SpawnRingMobs(
        Entity<WFFissureSpawnerComponent> ent,
        Entity<MapGridComponent> ground,
        EntityUid anchor,
        List<Vector2i> chosen)
    {
        var comp = ent.Comp;

        // A world with no faction still gets its decals, its burst and its crack sound - it just has nothing to send up.
        if (comp.Faction is not { } id || !_proto.TryIndex(id, out var faction))
            return;

        var want = _random.Next(comp.MinMobs, comp.MaxMobs + 1);

        if (!comp.Sanctioned)
            want = (int)MathF.Ceiling(want * comp.UnsanctionedMultiplier);

        want = Math.Min(want, chosen.Count);

        // Cumulative: SpawnedTotal never comes down, so killing mobs doesn't re-open the budget.
        want = Math.Min(want, comp.Cap - comp.SpawnedTotal);

        if (want <= 0)
            return;

        for (var i = 0; i < want; i++)
        {
            if (TrySpawnThreat(ent, ground, anchor, faction, chosen[i]))
                comp.SpawnedTotal++;
        }
    }

    /// <summary>Rolls one mob out of the faction and puts it on a tile, returning false only if nothing was spawned.</summary>
    private bool TrySpawnThreat(
        Entity<WFFissureSpawnerComponent> ent,
        Entity<MapGridComponent> ground,
        EntityUid anchor,
        SalvageFactionPrototype faction,
        Vector2i index)
    {
        if (faction.MobGroups.Count == 0)
            return false;

        string? proto = null;

        // Bounded re-roll: a group can legitimately spawn nothing (amount: 0 entries).
        for (var attempt = 0; attempt < ent.Comp.MobRollRetries && proto is null; attempt++)
        {
            var group = RollGroup(faction);
            var spawns = EntitySpawnCollection.GetSpawns(group.Entries, _random);

            if (spawns.Count == 0)
                continue;

            // One prototype per fissure: the cap counts mobs, not groups.
            proto = _random.Pick(spawns);
        }

        if (proto is null)
            return false;

        var coords = new EntityCoordinates(ground.Owner, (Vector2)index + ground.Comp.TileSizeHalfVector);

        // What was already here, so the stamp pass only touches what this spawn created.
        _preSpawnBuffer.Clear();
        _lookup.GetEntitiesInRange(coords, 1.0f, _preSpawnBuffer, LookupFlags.Uncontained);

        // Ghost roles stripped, or faction mobs would flood the ghost-role panel every ring.
        var uid = EntityManager.CreateEntityUninitialized(proto, coords);
        RemComp<GhostTakeoverAvailableComponent>(uid);
        RemComp<GhostRoleComponent>(uid);
        EntityManager.InitializeAndStartEntity(uid);

        // Stamp by lookup: some factions spawn RandomSpawner markers that replace themselves with the real mob.
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(coords, 1.0f, _lookupBuffer, LookupFlags.Uncontained);

        foreach (var candidate in _lookupBuffer)
        {
            // Only what this call put here, once; the consumed marker is still terminating in the lookup.
            if (_preSpawnBuffer.Contains(candidate) ||
                TerminatingOrDeleted(candidate) ||
                ent.Comp.Spawned.Contains(candidate))
            {
                continue;
            }

            ent.Comp.Spawned.Add(candidate);

            // Mobs only: turrets carry an HTNComponent too, and re-rooting one would disable it.
            if (!HasComp<MobStateComponent>(candidate) || !TryComp<HTNComponent>(candidate, out var htn))
                continue;

            StampThreat(ent, candidate, htn, anchor);
        }

        return true;
    }

    /// <summary>Makes a spawned mob a site threat: anchor-first HTN root, aggro on the anchor, emerge sound.</summary>
    private void StampThreat(Entity<WFFissureSpawnerComponent> ent, EntityUid mob, HTNComponent htn, EntityUid anchor)
    {
        htn.RootTask = new HTNCompoundTask { Task = ThreatCompound };

        // Replan through HTNSystem so a live operator's steering is shut down properly.
        if (htn.Plan is { } plan)
        {
            _htn.ShutdownTask(plan.CurrentOperator, htn.Blackboard, HTNOperatorStatus.Failed);
            _htn.ShutdownPlan(htn);
            htn.Plan = null;
        }

        _htn.Replan(htn);

        // Half of the aggro; WFFissureTargets' TargetIsAliveOrNACon is the other, and either alone is inert.
        _npcFaction.AggroEntity(mob, anchor);

        _audio.PlayPvs(ent.Comp.EmergeSound, mob);

        ent.Comp.Live.Add(mob);
    }

    /// <summary>One weighted draw over the faction's mob groups (upstream's version ignores the weights).</summary>
    private SalvageMobGroup RollGroup(SalvageFactionPrototype faction)
    {
        var sum = 0f;

        foreach (var group in faction.MobGroups)
        {
            sum += group.Prob;
        }

        var roll = _random.NextFloat() * sum;
        var running = 0f;

        foreach (var group in faction.MobGroups)
        {
            running += group.Prob;

            if (running >= roll)
                return group;
        }

        return faction.MobGroups[^1];
    }
}
