using System.Numerics;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Fissures;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Fissures;

/// <summary>
/// The extraction surge: one last band of fissures on the disc's perimeter as the chunk comes out.
/// The only subscription lives in the main partial, ordered before WFPlanetChunkSystem.
/// </summary>
public sealed partial class WFFissureSpawnerSystem
{
    /// <summary>The cut finished: both anchors surge on their own side of the rim, before the disc is lifted.</summary>
    private void OnCrackCompleted(ref WFCrackCompletedEvent args)
    {
        // The same guard WFPlanetChunkSystem.OnCrackCompleted applies (WFPlanetChunkSystem.cs:70/:91); a ground
        // layer's map entity IS the grid, which is why one uid answers all three.
        if (!args.GroundMap.IsValid() ||
            !TryComp<MapGridComponent>(args.GroundMap, out var mapGrid) ||
            !TryComp<BiomeComponent>(args.GroundMap, out var biome))
        {
            return;
        }

        // AND EVERY OTHER PRECONDITION OF THE CUT. F8 runs BEFORE WFPlanetChunkSystem, which refuses the disc on three
        // further conditions after the ground guard (WFPlanetChunkSystem.cs:78-103): the hull already holds a chunk, the
        // planet is already cracked, or the berth will not resolve. WFCrackerSystem.CompleteCrack has no once-only guard
        // of its own, so without replicating them a refused raise would still pin rim tiles, stamp permanent stage-4
        // decals and put hostile mobs on the ground - and would do it all again on the next raise. ForceSurge is
        // deliberately exempt: it is an admin verb.
        if (!TryComp<WFPlanetCrackerComponent>(args.Cracker, out var crackerComp))
            return;

        var cracker = new Entity<WFPlanetCrackerComponent>(args.Cracker, crackerComp);

        if (_chunks.TryGetChunk(cracker, out _) ||
            _crackers.IsPlanetCracked(cracker) ||
            !_crackers.TryGetBerthCentre(cracker, out _))
        {
            return;
        }

        var ground = new Entity<MapGridComponent>(args.GroundMap, mapGrid);

        Surge(args.AnchorA, ground, biome, args.CentreXY, args.Radius);
        Surge(args.AnchorB, ground, biome, args.CentreXY, args.Radius);
    }

    /// <summary>
    /// One anchor's share of the surge, returning how many threats it put on the ground.
    /// SELF-SUFFICIENT BY DESIGN. No path that reaches extraction raises WFAnchorDrillStartedEvent today:
    /// PlanetCrackerFixture.DeployPair(drill: true) and `wfcracker complete drill` both go Paired -> Locked through
    /// CompleteDrill (WFGravityAnchorSystem.Control.cs:14-28), which raises only WFAnchorDrillFinishedEvent. So this
    /// must not assume the spawner was ever armed - it takes the ground from the event, re-reads the anchor's centre
    /// now, and resolves the faction itself when none is cached. On an unarmed spawner the cached Faction is null (so
    /// nothing would spawn at all) and the cached Centre is Vector2.Zero (so both anchors would sort the rim by
    /// distance to the grid origin and surge on the same side).
    /// </summary>
    private int Surge(EntityUid anchor, Entity<MapGridComponent> ground, BiomeComponent biome, Vector2 discCentre, float radius)
    {
        if (!TryComp<WFFissureSpawnerComponent>(anchor, out var comp))
            return 0;

        var ent = new Entity<WFFissureSpawnerComponent>(anchor, comp);

        comp.Ground = ground.Owner;

        // Read now rather than from comp.Centre: the surge runs before TryExtract, so MoveRiders has not moved the
        // anchor onto the chunk yet and this is still its position on the planet.
        var centre = _transform.GetWorldPosition(Transform(anchor));

        // The cached pair is only a fast path; a spawner that was never armed resolves the world for itself.
        if (comp.Faction is null && TryGetFaction(ground.Owner, out var faction, out var sanctioned))
        {
            comp.Faction = faction;
            comp.Sanctioned = sanctioned;
        }

        var budget = comp.SurgeMobs;

        if (!comp.Sanctioned)
            budget = (int)MathF.Ceiling(budget * comp.UnsanctionedMultiplier);

        // THE PERIMETER, NEVER THE DISC. The band (Radius, Radius + 1] is outside the cut circle, so TryExtract's
        // MoveRiders (WFPlanetChunkSystem.Extraction.cs:281) leaves these mobs on the planet and the hole stamp
        // (:169) never turns these tiles empty and takes their decals with it.
        BuildBand(ground, discCentre, radius, radius + 1f);

        // Shuffled first so equidistant tiles are not always taken in enumeration order, then sorted so each anchor
        // surges out of its own side of the ring.
        _random.Shuffle(_ringBuffer);

        var half = ground.Comp.TileSizeHalfVector;
        _ringBuffer.Sort((x, y) =>
            ((Vector2)x + half - centre).LengthSquared().CompareTo(((Vector2)y + half - centre).LengthSquared()));

        _chosen.Clear();

        foreach (var index in _ringBuffer)
        {
            if (_chosen.Count >= budget)
                break;

            if (!EnsureTile(ground, index))
                continue;

            if (!_anchorable.TileFree(ground, index, (int)CollisionGroup.MachineLayer, (int)CollisionGroup.MachineLayer))
                continue;

            _chosen.Add(index);
        }

        if (_chosen.Count == 0)
            return 0;

        // Pinning rim indices before TryExtract is harmless: its own ReserveTiles over the enlarged aabb
        // (Extraction.cs:113) pins-and-skips an existing non-empty tile, and it pins the rim again at :176.
        _biome.WfPinTiles((ground.Owner, biome), _chosen);

        // Stage four: a surge fissure is fully grown the moment it opens.
        StampFissures(ground, comp, discCentre, 4);

        SalvageFactionPrototype? factionProto = null;

        if (comp.Faction is { } id)
            _proto.TryIndex(id, out factionProto);

        var spawned = 0;

        if (factionProto is not null)
        {
            // The surge budget is separate from Cap, so a fully drilled-out anchor still gets its send-off.
            foreach (var index in _chosen)
            {
                if (!TrySpawnThreat(ent, ground, anchor, factionProto, index))
                    continue;

                comp.SurgeSpawned++;
                spawned++;
            }
        }

        _audio.PlayPvs(comp.CrackSound, new EntityCoordinates(ground.Owner, centre));

        return spawned;
    }
}
