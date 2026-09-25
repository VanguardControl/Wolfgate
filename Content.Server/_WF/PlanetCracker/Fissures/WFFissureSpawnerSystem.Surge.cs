using System.Numerics;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Fissures;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Fissures;

/// <summary>The extraction surge: one last band of fissures on the disc's perimeter as the chunk comes out.</summary>
public sealed partial class WFFissureSpawnerSystem
{
    /// <summary>The cut finished: both anchors surge on their own side of the rim, before the disc is lifted.</summary>
    private void OnCrackCompleted(ref WFCrackCompletedEvent args)
    {
        // The chunk system's ground guard; a ground layer's map entity is its grid.
        if (!args.GroundMap.IsValid() ||
            !TryComp<MapGridComponent>(args.GroundMap, out var mapGrid) ||
            !TryComp<BiomeComponent>(args.GroundMap, out var biome))
        {
            return;
        }

        // The extraction's other preconditions too, so a refused cut doesn't surge; ForceSurge skips them.
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

    /// <summary>One anchor's share of the surge, even from an unarmed spawner; returns the threats spawned.</summary>
    private int Surge(EntityUid anchor, Entity<MapGridComponent> ground, BiomeComponent biome, Vector2 discCentre, float radius)
    {
        if (!TryComp<WFFissureSpawnerComponent>(anchor, out var comp))
            return 0;

        var ent = new Entity<WFFissureSpawnerComponent>(anchor, comp);

        comp.Ground = ground.Owner;

        // Read now, not from comp.Centre; the anchor hasn't ridden onto the chunk yet.
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

        // The perimeter, never the disc, so the mobs and decals stay on the planet.
        BuildBand(ground, discCentre, radius, radius + 1f);

        // Shuffled so ties vary, then sorted so each anchor surges from its own side of the ring.
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

        // Pinning before the extraction is harmless; it pins the rim again itself.
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
