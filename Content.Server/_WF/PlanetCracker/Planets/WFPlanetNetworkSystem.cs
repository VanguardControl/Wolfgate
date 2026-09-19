using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._DV.Planet;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Server.Shuttles.Events;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Atmos;
using Content.Shared.Parallax.Biomes;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetCracker.Planets;

/// <summary>
/// Builds and tears down planet z-map networks: a biome ground layer, fall-through air layers, a cloud layer and an orbit layer.
/// </summary>
public sealed partial class WFPlanetNetworkSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PlanetSystem _planet = default!;
    [Dependency] private TransformSystem _transform = default!;

    /// <summary>Marker spawned at the planet centre so a hull in orbit can see where the body underneath it is.</summary>
    private const string OrbitMarkerProto = "WFOrbitBeacon";

    private readonly List<(Vector2i Index, Tile Tile)> _reservedTiles = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // Broadcast, not directed: legal alongside the force-anchor, salvage and nukeops handlers.
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnConsoleFTLAttempt);
    }

    /// <summary>Refuses console FTL while a hull is riding a transit map between layers.</summary>
    private void OnConsoleFTLAttempt(ref ConsoleFTLAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (Transform(args.Uid).MapUid is not { } mapUid || !HasComp<CEZTransitMapComponent>(mapUid))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("wf-shuttle-console-in-transit");
    }

    /// <summary>
    /// Builds the network for a registered sector body and records it on the body.
    /// </summary>
    /// <param name="planet">The sector body carrying the surface definition.</param>
    /// <param name="network">The z-map network entity that was built.</param>
    public bool TryBuildNetwork(Entity<WFSectorPlanetComponent> planet, out EntityUid network)
    {
        network = EntityUid.Invalid;

        if (planet.Comp.Surface is not { } surfaceId)
            return false;

        if (!_proto.TryIndex(surfaceId, out var surface))
        {
            Log.Error($"Sector body {ToPrettyString(planet.Owner)} names unknown surface \"{surfaceId}\".");
            return false;
        }

        if (planet.Comp.Network is { } already && TryGetEntity(already, out _))
        {
            Log.Warning($"Sector body {ToPrettyString(planet.Owner)} already has a planet network.");
            return false;
        }

        // World frame only: the body is spawned with SpawnAtPosition, which runs grid traversal, so its parent
        // is not guaranteed to be the sector map and a local-frame position could silently become grid-local.
        var centre = _transform.GetWorldPosition(planet.Owner);
        var displayName = MetaData(planet.Owner).EntityName;

        if (BuildNetwork(surface, centre, displayName, planet.Owner) is not { } built)
            return false;

        network = built;
        planet.Comp.Network = GetNetEntity(built);
        planet.Comp.OrbitMap = GetNetEntity(Comp<WFPlanetNetworkComponent>(built).OrbitMap);
        Dirty(planet);
        return true;
    }

    /// <summary>
    /// Builds a whole planet z-stack and returns its network entity, or null when the build failed.
    /// </summary>
    /// <param name="surface">The surface definition describing the stack.</param>
    /// <param name="centre">Planet centre in the world frame; the orbit marker and the console's range gate both measure here.</param>
    /// <param name="displayName">Name substituted into the map, network and marker locale strings.</param>
    /// <param name="planetEntity">The sector body this network belongs to, if any.</param>
    public EntityUid? BuildNetwork(WFPlanetSurfacePrototype surface, Vector2 centre, string displayName, EntityUid? planetEntity)
    {
        if (!_cfg.GetCVar(PlanetCrackerCVars.PlanetNetworks))
        {
            Log.Warning($"Refused to build a planet network for \"{surface.ID}\": wf.planet_networks is off.");
            return null;
        }

        var groundProto = _proto.Index(surface.Ground);

        // Every map stays uninitialised until InitializeZNetwork, so the network registry lands on all of them at once.
        var ground = _planet.SpawnPlanet(surface.Ground, runMapInit: false);

        // Fix the biome seed before any chunk or marker chunk can load, so a planet's terrain and its deep veins are
        // identical every round. EnsurePlanet rolls _random.Next() when SpawnPlanet passes no seed
        // (Content.Server/Parallax/BiomeSystem.PlanetSetup.cs:33).
        if (surface.Seed is { } biomeSeed && TryComp<BiomeComponent>(ground, out var groundBiome))
            _biome.SetSeed(ground, groundBiome, biomeSeed);

        if (surface.GroundGrid is { } gridPath)
        {
            var groundMapId = Comp<MapComponent>(ground).MapId;

            if (!_mapLoader.TryLoadGrid(groundMapId, gridPath, out var grid, offset: centre))
            {
                Log.Error($"Failed to load ground grid {gridPath} for planet surface \"{surface.ID}\".");
                Del(ground);
                return null;
            }

            // Don't let the biome plant rocks inside the hand-made base.
            _reservedTiles.Clear();
            var aabb = Comp<MapGridComponent>(grid.Value.Owner).LocalAABB.Translated(centre);
            _biome.ReserveTiles(ground, aabb.Enlarged(0.2f), _reservedTiles);
        }

        var layers = new List<EntityUid> { ground };

        for (var i = 0; i < surface.AirLayers; i++)
        {
            layers.Add(_map.CreateMap(out _, runMapInit: false));
        }

        var cloud = EntityUid.Invalid;
        if (surface.CloudLayer)
        {
            cloud = _map.CreateMap(out _, runMapInit: false);
            layers.Add(cloud);
        }

        var orbit = _map.CreateMap(out _, runMapInit: false);
        layers.Add(orbit);

        var network = _zLevels.CreateMapNetwork(surface.NetworkComponents);
        _meta.SetEntityName(network, Loc.GetString("wf-planet-network-name", ("planet", displayName)));

        // One depth per call, ascending from 0: the network's sorted cache only takes its first-element
        // branch at depth 0, so depth 0 must be added first and the depths must expand contiguously.
        for (var depth = 0; depth < layers.Count; depth++)
        {
            if (_zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int> { { layers[depth], depth } }))
                continue;

            Log.Error($"Failed to add depth {depth} to the planet network for \"{surface.ID}\"; unwinding.");
            _zLevels.DeleteMapNetwork(network);

            foreach (var layer in layers)
            {
                QueueDel(layer);
            }

            return null;
        }

        _zLevels.InitializeZNetwork(network);

        // Post-init fixups, last write wins: the network registry was stamped over every layer at MapInit
        // with removeExisting defaulted to true, so anything per-layer has to be re-applied here.
        var networkNet = GetNetEntity(network);

        foreach (var layer in layers)
        {
            var marker = EnsureComp<WFPlanetLayerComponent>(layer);
            marker.Network = networkNet;
            marker.Gravity = surface.Gravity;
            marker.MaxSpeed = surface.AirMaxSpeed;
            marker.LinearDamping = surface.AirDamping;
            Dirty(layer, marker);
        }

        WFPlanetAmbiencePrototype? ambience = null;
        foreach (var candidate in _proto.EnumeratePrototypes<WFPlanetAmbiencePrototype>())
        {
            if (candidate.PlanetType != surface.PlanetType)
                continue;

            ambience = candidate;
            break;
        }

        if (ambience != null)
        {
            var orbitDepth = layers.Count - 1;
            for (var depth = 0; depth < layers.Count; depth++)
            {
                var layerAmbience = EnsureComp<WFPlanetAmbienceComponent>(layers[depth]);
                layerAmbience.Profile = ambience.ID;
                layerAmbience.VolumeOffset = WFPlanetAmbience.LayerVolumeOffset(depth, orbitDepth);
                Dirty(layers[depth], layerAmbience);
            }
        }

        _atmos.SetMapAtmosphere(ground, false, groundProto.Atmosphere);

        if (surface.GroundComponents is { } groundComponents)
            EntityManager.AddComponents(ground, groundComponents, removeExisting: false);

        if (surface.AirComponents is { } airComponents)
        {
            for (var depth = 1; depth <= surface.AirLayers; depth++)
            {
                EntityManager.AddComponents(layers[depth], airComponents, removeExisting: false);
            }
        }

        if (surface.CloudLayer)
        {
            if (surface.CloudComponents is { } cloudComponents)
                EntityManager.AddComponents(cloud, cloudComponents, removeExisting: false);

            EnsureComp<CEZCloudLayerComponent>(cloud);
        }

        _atmos.SetMapAtmosphere(orbit, true, GasMixture.SpaceGas);
        RemComp<MapLightComponent>(orbit);

        if (surface.OrbitComponents is { } orbitComponents)
            EntityManager.AddComponents(orbit, orbitComponents, removeExisting: false);

        var orbitLayer = EnsureComp<WFOrbitLayerComponent>(orbit);
        orbitLayer.Planet = planetEntity is { } planetUid ? GetNetEntity(planetUid) : null;
        orbitLayer.Range = surface.OrbitRange;
        orbitLayer.MaxSpeed = surface.OrbitMaxSpeed;
        orbitLayer.LinearDamping = surface.OrbitDamping;
        orbitLayer.Network = networkNet;
        orbitLayer.RadarGround = GetNetEntity(ground);
        if (TryComp<BiomeComponent>(ground, out var radarBiome))
        {
            orbitLayer.RadarLayers = new(radarBiome.Layers);
            orbitLayer.RadarSeed = radarBiome.Seed;
        }
        Dirty(orbit, orbitLayer);

        _meta.SetEntityName(orbit, Loc.GetString(surface.OrbitMapName, ("planet", displayName)));

        // Deliberately NOT an FTLDestination: orbit is entered from the shuttle console's own button, which needs no
        // drive (WFOrbitEntrySystem). The marker below is a warp point and a radar label, never a jump target.
        var orbitMarker = SpawnAtPosition(OrbitMarkerProto, new EntityCoordinates(orbit, centre));
        _meta.SetEntityName(orbitMarker, Loc.GetString(surface.OrbitMarkerName, ("planet", displayName)));

        // A mapgrid on the orbit layer disables every arriving hull and blocks climbs into it.
        if (HasComp<MapGridComponent>(orbit))
            Log.Error($"The orbit layer of \"{surface.ID}\" has a MapGridComponent; arriving shuttles will be disabled and climbs will be blocked.");

        var comp = EnsureComp<WFPlanetNetworkComponent>(network);
        comp.Planet = planetEntity;
        comp.GroundMap = ground;
        comp.OrbitMap = orbit;
        comp.Layers = layers;
        comp.Surface = surface.ID;
        comp.Centre = centre;
        EntityManager.System<WFPlanetWeatherSystem>().Configure(network, surface, displayName);

        return network.Owner;
    }

    /// <summary>
    /// Queues every layer and the network entity for deletion, and clears the owning sector body's record of it.
    /// </summary>
    /// <param name="network">The z-map network entity.</param>
    public void DeleteNetwork(EntityUid network)
    {
        if (TryComp<WFPlanetNetworkComponent>(network, out var comp)
            && comp.Planet is { } planetUid
            && TryComp<WFSectorPlanetComponent>(planetUid, out var sector))
        {
            sector.Network = null;
            sector.OrbitMap = null;

            if (!TerminatingOrDeleted(planetUid))
                Dirty(planetUid, sector);
        }

        _zLevels.DeleteMapNetwork(network);
    }
}
