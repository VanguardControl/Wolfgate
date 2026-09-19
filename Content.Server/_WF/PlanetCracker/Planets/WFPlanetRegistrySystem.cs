using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetCracker.Planets;

/// <summary>
/// The round-scoped registry of sector bodies that have a Wolfgate surface, and the round-start build hook.
/// </summary>
public sealed partial class WFPlanetRegistrySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private WFPlanetNetworkSystem _networks = default!;

    private readonly Dictionary<ProtoId<PlanetTypePrototype>, WFPlanetSurfacePrototype> _surfaces = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        foreach (var surface in _proto.EnumeratePrototypes<WFPlanetSurfacePrototype>())
        {
            if (_surfaces.TryGetValue(surface.PlanetType, out var existing))
            {
                Log.Warning($"Planet type \"{surface.PlanetType}\" has two surfaces, \"{existing.ID}\" and \"{surface.ID}\"; keeping the first.");
                continue;
            }

            _surfaces[surface.PlanetType] = surface;
        }

        SubscribeLocalEvent<WFSectorPlanetComponent, ComponentShutdown>(OnSectorPlanetShutdown);
    }

    /// <summary>
    /// Registers a freshly spawned sector body, and builds its network when the surface asks for it at round start.
    /// </summary>
    /// <param name="map">The sector map the body was spawned on.</param>
    /// <param name="planetEntity">The spawned body.</param>
    /// <param name="planet">The runtime helper the body was spawned from.</param>
    public void RegisterPlanet(Entity<StarSystemMapComponent> map, EntityUid planetEntity, Planet planet)
    {
        if (!_cfg.GetCVar(PlanetCrackerCVars.PlanetNetworks))
            return;

        if (map.Comp.System is not { } systemId || !_proto.TryIndex(systemId, out var systemProto))
            return;

        // The body carries no prototype id, so match it back to its star-system entry by recomputing the
        // spawn position with the identical expression the system builder uses. The maths is bit-exact.
        StarSystemPlanet? entry = null;

        foreach (var candidate in systemProto.Planets)
        {
            var position = new Vector2(MathF.Cos(candidate.Angle), MathF.Sin(candidate.Angle)) * candidate.Distance;

            if (position != planet.Position)
                continue;

            entry = candidate;
            break;
        }

        if (entry is null)
        {
            foreach (var candidate in systemProto.Planets)
            {
                if (!_proto.TryIndex(candidate.Planet, out var typeProto) || typeProto.Name != planet.Name)
                    continue;

                entry = candidate;
                Log.Warning($"Sector body {ToPrettyString(planetEntity)} matched no {systemId} entry by position; matched \"{planet.Name}\" by name instead.");
                break;
            }
        }

        if (entry is null)
        {
            Log.Error($"Sector body {ToPrettyString(planetEntity)} matches no planet entry of star system \"{systemId}\".");
            return;
        }

        if (!_surfaces.TryGetValue(entry.Planet, out var surface))
            return;

        var body = ApplySurface(planetEntity, surface);

        if (surface.BuildAtRoundStart)
            _networks.TryBuildNetwork(body, out _);
    }

    /// <summary>
    /// Mirrors a surface definition onto a sector body. This is the single writer of Surface and Sanctioned in
    /// production code, so a console never has to index the prototype to learn whether cracking the world is legal;
    /// the one test-side writer (PlanetNetworkTest.BuildForSectorBody) is converted to it by F2's test stage.
    /// </summary>
    /// <param name="body">The sector body to stamp.</param>
    /// <param name="surface">The surface definition to mirror.</param>
    public Entity<WFSectorPlanetComponent> ApplySurface(EntityUid body, WFPlanetSurfacePrototype surface)
    {
        var comp = EnsureComp<WFSectorPlanetComponent>(body);
        comp.Surface = surface.ID;
        comp.Sanctioned = surface.Sanctioned;
        Dirty(body, comp);

        return (body, comp);
    }

    /// <summary>Every registered sector body this round.</summary>
    public List<Entity<WFSectorPlanetComponent>> GetPlanets()
    {
        var planets = new List<Entity<WFSectorPlanetComponent>>();
        var query = AllEntityQuery<WFSectorPlanetComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            planets.Add((uid, comp));
        }

        return planets;
    }

    /// <summary>Finds a registered sector body by its display name, case-insensitively.</summary>
    /// <param name="name">Display name of the body.</param>
    /// <param name="planet">The matching body.</param>
    public bool TryGetPlanetByName(string name, out Entity<WFSectorPlanetComponent> planet)
    {
        var query = AllEntityQuery<WFSectorPlanetComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (!string.Equals(MetaData(uid).EntityName, name, StringComparison.OrdinalIgnoreCase))
                continue;

            planet = (uid, comp);
            return true;
        }

        planet = default;
        return false;
    }

    /// <summary>Finds the surface definition registered for a sector body type, if there is one.</summary>
    /// <param name="planetType">The body type to look up.</param>
    /// <param name="surface">The surface definition for that type.</param>
    public bool TryGetSurface(ProtoId<PlanetTypePrototype> planetType, [NotNullWhen(true)] out WFPlanetSurfacePrototype? surface)
    {
        return _surfaces.TryGetValue(planetType, out surface);
    }

    /// <summary>Tears the body's network down with it, so nothing outlives the sector map.</summary>
    private void OnSectorPlanetShutdown(Entity<WFSectorPlanetComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Network is not { } network || !TryGetEntity(network, out var networkUid))
            return;

        _networks.DeleteNetwork(networkUid.Value);
    }
}
