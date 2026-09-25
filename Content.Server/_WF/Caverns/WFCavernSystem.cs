using System.Diagnostics.CodeAnalysis;
using Content.Server._DV.Planet;
using Content.Server._WF.Planets;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Light.Components;
using Content.Shared.Parallax;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Caverns;

/// <summary>Puts a cavern below every planet whose surface has one, and fits it out once the network is built.</summary>
public sealed class WFCavernSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private PlanetSystem _planet = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFPlanetLowerLayersEvent>(OnLowerLayers);
        SubscribeLocalEvent<WFPlanetNetworkBuiltEvent>(OnNetworkBuilt);
    }

    /// <summary>The cavern under a surface, if it has one.</summary>
    public bool TryGetCavern(ProtoId<WFPlanetSurfacePrototype> surface, [NotNullWhen(true)] out WFCavernPrototype? cavern)
    {
        foreach (var candidate in _proto.EnumeratePrototypes<WFCavernPrototype>())
        {
            if (candidate.Surface != surface)
                continue;

            cavern = candidate;
            return true;
        }

        cavern = null;
        return false;
    }

    /// <summary>Spawns the cavern map, uninitialised, as depth -1.</summary>
    private void OnLowerLayers(ref WFPlanetLowerLayersEvent args)
    {
        if (!_cfg.GetCVar(CavernCVars.Caverns) || !TryGetCavern(args.Surface.ID, out var cavern))
            return;

        var level = _planet.SpawnPlanet(cavern.Level, runMapInit: false);

        // Before any chunk loads, so the same world always has the same cavern.
        if (TryComp<BiomeComponent>(level, out var biome))
        {
            var surfaceSeed = args.Surface.Seed ?? CompOrNull<BiomeComponent>(args.Ground)?.Seed ?? 0;
            _biome.SetSeed(level, biome, unchecked(surfaceSeed + cavern.SeedOffset));
        }

        var layer = EnsureComp<WFCavernLayerComponent>(level);
        layer.Cavern = cavern.ID;
        layer.Ground = args.Ground;
        Dirty(level, layer);

        args.Lower.Add(level);
    }

    /// <summary>Gives each cavern its own air, takes away the sky and links the ground to it.</summary>
    private void OnNetworkBuilt(ref WFPlanetNetworkBuiltEvent args)
    {
        foreach (var map in args.Lower)
        {
            if (!TryComp<WFCavernLayerComponent>(map, out var layer) || !_proto.TryIndex(layer.Cavern, out var cavern))
                continue;

            // InitializeZNetwork stamped the surface's network air over every member.
            _atmos.SetMapAtmosphere(map, false, _proto.Index(cavern.Level).Atmosphere);

            RemComp<LightCycleComponent>(map);
            RemComp<SunShadowComponent>(map);
            RemComp<SunShadowCycleComponent>(map);
            RemComp<ParallaxComponent>(map);

            var roof = EnsureComp<RoofComponent>(map);
            roof.Color = cavern.RoofColor;
            Dirty(map, roof);

            var ground = EnsureComp<WFCavernGroundComponent>(args.Ground);
            ground.Cavern = map;
            ground.Prototype = cavern.ID;
        }
    }
}
