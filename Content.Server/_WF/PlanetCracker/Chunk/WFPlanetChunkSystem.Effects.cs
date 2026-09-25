using System.Numerics;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Gravity;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.PlanetCracker.Chunk;

/// <summary>Extraction effects on both layers; every call is guarded so a failure costs only the effect.</summary>
public sealed partial class WFPlanetChunkSystem
{
    private const string BurstEffect = "WFEffectChunkBurst";

    /// <summary>Twice the cut's periodic site kick.</summary>
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
        // StartGridShake needs a GravityComponent; the gridless orbit map can't shake.
        if (TryComp<GravityComponent>(cracker.Owner, out var hullGravity))
            _gravity.StartGridShake(cracker.Owner, hullGravity);

        if (TryComp<GravityComponent>(chunk, out var chunkGravity))
            _gravity.StartGridShake(chunk, chunkGravity);

        var groundMapId = Transform(groundMap).MapID;
        var orbitMapId = Transform(orbitMap).MapID;

        // The client zeroes positional gain across maps, so the boom is global and the positional copies are extra.
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
