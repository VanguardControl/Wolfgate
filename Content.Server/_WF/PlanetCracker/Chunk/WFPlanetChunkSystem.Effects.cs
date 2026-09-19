using System.Numerics;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Gravity;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.PlanetCracker.Chunk;

/// <summary>
/// The moment the disc tears free, on both layers at once. Nothing here is load-bearing: every call is guarded so a
/// missing gravity component or a gridless map costs the effect and not the extraction.
/// </summary>
public sealed partial class WFPlanetChunkSystem
{
    /// <summary>The burst sprite spawned at the hole and at the chunk.</summary>
    private const string BurstEffect = "WFEffectChunkBurst";

    /// <summary>The extraction kick is twice the site kick the cut has been throwing every couple of seconds.</summary>
    private const float ExtractKickStrength = 2f;

    /// <summary>Shakes, the boom on both layers, the burst sprites and one hard camera kick over the site.</summary>
    private void PlayExtractionEffects(
        Entity<WFPlanetCrackerComponent> cracker,
        EntityUid chunk,
        EntityUid groundMap,
        EntityUid orbitMap,
        Vector2 centre,
        float radius)
    {
        // StartGridShake needs a GravityComponent and silently does nothing without one. Never on the orbit map: the
        // client's own guard means a player parented to a gridless map is never kicked, and the orbit map has no grid.
        if (TryComp<GravityComponent>(cracker.Owner, out var hullGravity))
            _gravity.StartGridShake(cracker.Owner, hullGravity);

        if (TryComp<GravityComponent>(chunk, out var chunkGravity))
            _gravity.StartGridShake(chunk, chunkGravity);

        var groundMapId = Transform(groundMap).MapID;
        var orbitMapId = Transform(orbitMap).MapID;

        // One audio entity heard on two layers: a positional sound is replicated across the z-eyes but the client
        // zeroes its gain across maps, so the boom is global and the positional copies are extra.
        _audio.PlayGlobal(
            cracker.Comp.ExtractSound,
            Filter.Empty().AddInMap(orbitMapId, EntityManager).AddInMap(groundMapId, EntityManager),
            true);

        _audio.PlayGlobal(
            cracker.Comp.ExtractSound2,
            Filter.Empty().AddInMap(orbitMapId, EntityManager).AddInMap(groundMapId, EntityManager),
            true);
        _audio.PlayPvs(cracker.Comp.ExtractSound, new EntityCoordinates(groundMap, centre));
        _audio.PlayPvs(cracker.Comp.ExtractSound2, new EntityCoordinates(groundMap, centre));
        _audio.PlayPvs(cracker.Comp.ExtractSound, new EntityCoordinates(chunk, centre));
        _audio.PlayPvs(cracker.Comp.ExtractSound2, new EntityCoordinates(chunk, centre));

        // The chunk's tile indices are identical to the ground's, so the disc centre is the same local vector on both.
        Spawn(BurstEffect, new EntityCoordinates(groundMap, centre));
        Spawn(BurstEffect, new EntityCoordinates(chunk, centre));

        _crackers.KickCamerasInRange(
            new MapCoordinates(centre, groundMapId),
            radius + cracker.Comp.SiteKickPadding,
            ExtractKickStrength);
    }
}
