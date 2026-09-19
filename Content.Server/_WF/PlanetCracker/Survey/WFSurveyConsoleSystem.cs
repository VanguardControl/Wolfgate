using System.Numerics;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Server.Shuttles.Components;
using Content.Server.Warps;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared._WF.PlanetCracker.Survey.BUI;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Survey;

/// <summary>
/// Server half of the sector survey console. It authors the whole window state once a second and drives the console's
/// screen face; it handles no messages at all, because the "go here" a pilot needs is each sector body's own
/// pre-existing FTL beacon name - StarSystemMapSystem already spawns one FTLBeacon-carrying body per star-system entry
/// and renames it to the planet's name (Content.Server/_FarHorizons/StarSystem/StarSystemMapSystem.cs:57-66), which is
/// exactly the string ShuttleConsoleSystem.GetBeacons shows in the destination tree (ShuttleConsoleSystem.FTL.cs:93-109).
/// Nothing has to be spawned, renamed or refreshed to make a row usable, so the interface is read-only.
/// </summary>
public sealed partial class WFSurveyConsoleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedWFCrackerSystem _crackers = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    /// <summary>Push interval. One tier only: nothing on this window counts down.</summary>
    private static readonly TimeSpan PushInterval = TimeSpan.FromSeconds(1);

    /// <summary>Consoles with the window open; pruned by polling the UI each sweep.</summary>
    private readonly HashSet<EntityUid> _listeners = new();

    /// <summary>Listeners that have gone away, collected before removal so the set is not mutated while read.</summary>
    private readonly List<EntityUid> _stale = new();

    /// <summary>Rows of one state build, reused so a sweep allocates nothing per console.</summary>
    private readonly List<WFSurveyPlanetRow> _rowBuffer = new();

    /// <summary>Registered sector bodies keyed by their surface's planet type, rebuilt per state build.</summary>
    private readonly Dictionary<string, Entity<WFSectorPlanetComponent>> _registryBuffer = new();

    /// <summary>Every sector body on the star-system map keyed by its display name, rebuilt per state build.</summary>
    private readonly Dictionary<string, EntityUid> _bodyBuffer = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Next push.</summary>
    private TimeSpan _nextUpdate;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // The only subscription this system adds. Unclaimed: nothing else in the codebase subscribes a directed event
        // on WFSectorSurveyConsoleComponent. There is no Subs.BuiEvents block because the console sends no messages,
        // and nothing is subscribed on WFSectorPlanetComponent - WFPlanetRegistrySystem owns its only directed pair
        // (WFPlanetRegistrySystem.cs:40).
        SubscribeLocalEvent<WFSectorSurveyConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
    }

    /// <summary>A freshly opened window starts listening and gets its first state at once rather than up to a second late.</summary>
    private void OnUiOpened(EntityUid uid, WFSectorSurveyConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (!Equals(args.UiKey, WFSurveyConsoleUiKey.Key))
            return;

        _listeners.Add(uid);
        _ui.SetUiState(uid, WFSurveyConsoleUiKey.Key, BuildState(uid));
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        PruneListeners();

        foreach (var console in _listeners)
        {
            _ui.SetUiState(console, WFSurveyConsoleUiKey.Key, BuildState(console));
        }

        // Cheap even with no window open: SetData early-returns when the value has not moved, and this is what gives a
        // console its first screen face.
        UpdateScreens();

        _nextUpdate = _timing.CurTime + PushInterval;
    }

    /// <summary>
    /// Drops consoles whose window has closed.
    /// Polled rather than subscribed: BoundUIClosedEvent on this component would be a second directed subscription on a
    /// pair another system may claim later, and Robust allows only one server-wide.
    /// </summary>
    private void PruneListeners()
    {
        _stale.Clear();

        foreach (var console in _listeners)
        {
            if (!Exists(console) || !_ui.IsUiOpen(console, WFSurveyConsoleUiKey.Key))
                _stale.Add(console);
        }

        foreach (var console in _stale)
        {
            _listeners.Remove(console);
        }
    }

    /// <summary>
    /// Everything the window draws, in one server-side pass.
    /// Public so a test may read exactly what a client would receive without standing a window up.
    /// </summary>
    public WFSurveyConsoleState BuildState(EntityUid console)
    {
        var state = new WFSurveyConsoleState();

        var positionKnown = TryGetConsoleSectorPosition(console, out var consolePos);
        state.ConsolePositionKnown = positionKnown;

        if (!TryGetSectorMap(out var sectorMap, out var systemProto))
            return state;

        // StarSystemPrototype carries no display name field, so its id is the only name there is to show.
        state.SystemName = systemProto.ID;

        BuildRegistry();
        BuildBodies(sectorMap);

        _rowBuffer.Clear();

        foreach (var entry in systemProto.Planets)
        {
            // Bit-identical to SharedStarSystemMapSystem.cs:26 and WFPlanetRegistrySystem.cs:62. The body carries no
            // prototype id, so recomputing its spawn position with the same expression is how a row is matched back to
            // its star-system entry; any drift here silently unpairs every registered world.
            var pos = new Vector2(MathF.Cos(entry.Angle), MathF.Sin(entry.Angle)) * entry.Distance;

            var name = _proto.TryIndex(entry.Planet, out var typeProto) ? typeProto.Name : entry.Planet.Id;

            NetEntity? planet = null;
            var hasBeacon = false;
            var hasSurface = false;
            var sanctioned = true;
            var cracked = false;
            var veins = WFVeinRating.Poor;
            var veinsKnown = false;

            if (_registryBuffer.TryGetValue(entry.Planet.Id, out var body))
            {
                planet = GetNetEntity(body.Owner);
                name = MetaData(body.Owner).EntityName;
                hasBeacon = HasComp<FTLBeaconComponent>(body.Owner);
                hasSurface = true;
                sanctioned = body.Comp.Sanctioned;
                cracked = body.Comp.Cracked;
                veinsKnown = TryRate(body.Comp, sanctioned, out veins);
            }
            else if (_bodyBuffer.TryGetValue(name, out var unregistered))
            {
                // An unregistered body is still a spawned PlanetEntity and still a beacon: registration is gated on
                // wf.planet_networks, which is SERVERONLY and defaults false (PlanetCrackerCVars.cs:15), so on a stock
                // server every row would otherwise read "no destination" about a body that has carried an FTL beacon
                // since round start. Planet stays null - that field means "registered body", not "reachable body".
                hasBeacon = HasComp<FTLBeaconComponent>(unregistered);
            }

            // Raw Vector2 subtraction, never MapCoordinates.InRange: that one returns false for differing MapIds before
            // it does any maths (F4 D-H), and the console is routinely on a different map from the sector frame.
            var distance = positionKnown ? (pos - consolePos).Length() : 0f;

            _rowBuffer.Add(new WFSurveyPlanetRow(
                planet,
                name,
                name,
                hasBeacon,
                pos,
                distance,
                positionKnown,
                hasSurface,
                sanctioned,
                cracked,
                veins,
                veinsKnown));
        }

        if (positionKnown)
            _rowBuffer.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        else
            _rowBuffer.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        state.Planets.AddRange(_rowBuffer);

        return state;
    }

    /// <summary>
    /// The console's position in the SECTOR frame, which is the only frame the rows are measured in.
    /// A planet network's orbit, air and ground maps only coincidentally share the sector XY frame, because
    /// BuildNetwork places everything at the body's centre (WFPlanetNetworkSystem.cs:230,:243); a raw cross-map
    /// subtraction taken from an orbiting grid is therefore wrong on FTL, transit and POI maps, and those return false
    /// here so every row reads unknown rather than a meaningless number.
    /// </summary>
    private bool TryGetConsoleSectorPosition(EntityUid console, out Vector2 position)
    {
        position = Vector2.Zero;

        var xform = Transform(console);

        if (xform.MapUid is not { } mapUid)
            return false;

        if (HasComp<WFOrbitLayerComponent>(mapUid) || HasComp<WFPlanetLayerComponent>(mapUid))
        {
            if (!TryGetAnchorBody(mapUid, out var body))
                return false;

            position = _transform.GetWorldPosition(body);
            return true;
        }

        if (!HasComp<StarSystemMapComponent>(mapUid))
            return false;

        // The grid's own world position where the console rests on one, the console's otherwise - the same walk
        // WFCrackConsoleSystem.cs:231-237 does.
        position = _transform.GetWorldPosition(xform.GridUid ?? console);
        return true;
    }

    /// <summary>The sector body a planet-network layer belongs to, resolved from orbit first and from the network otherwise.</summary>
    private bool TryGetAnchorBody(EntityUid mapUid, out EntityUid body)
    {
        if (_crackers.TryGetPlanetFromOrbit(mapUid, out var orbited))
        {
            body = orbited.Owner;
            return true;
        }

        body = default;

        if (!TryComp<WFPlanetLayerComponent>(mapUid, out var layer))
            return false;

        if (layer.Network is not { } network || !TryGetEntity(network, out var networkUid))
            return false;

        if (!TryComp<WFPlanetNetworkComponent>(networkUid, out var networkComp) || networkComp.Planet is not { } planet)
            return false;

        body = planet;
        return true;
    }

    /// <summary>The first star-system map in the world that has a system assigned.</summary>
    private bool TryGetSectorMap(out EntityUid map, out StarSystemPrototype systemProto)
    {
        map = default;
        systemProto = default!;

        var query = AllEntityQuery<StarSystemMapComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.System is not { } systemId || !_proto.TryIndex(systemId, out var proto))
                continue;

            map = uid;
            systemProto = proto;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Indexes every sector body on the star-system map by its display name, registered or not.
    /// The bodies are the map's own direct children - StarSystemMapSystem spawns each one at
    /// EntityCoordinates(map, position) and renames it to the planet's name (StarSystemMapSystem.cs:62-65) - so a
    /// warp point parented to a grid, which is what a station's own warp points are, is never picked up here.
    /// Identity is WarpPointComponent rather than FTLBeaconComponent on purpose: the row's HasBeacon is the regression
    /// guard for the beacon itself, and keying the lookup on it would make that guard vacuously true.
    /// </summary>
    private void BuildBodies(EntityUid map)
    {
        _bodyBuffer.Clear();

        var query = AllEntityQuery<WarpPointComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.ParentUid != map)
                continue;

            _bodyBuffer[MetaData(uid).EntityName] = uid;
        }
    }

    /// <summary>
    /// Indexes every registered sector body by its surface's planet type, in one pass.
    /// WFPlanetRegistrySystem.GetPlanets() is deliberately not used: it allocates a fresh list on every call
    /// (WFPlanetRegistrySystem.cs:102-114), and this runs once a second per open console.
    /// </summary>
    private void BuildRegistry()
    {
        _registryBuffer.Clear();

        var query = AllEntityQuery<WFSectorPlanetComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Surface is not { } surfaceId || !_proto.TryIndex(surfaceId, out var surface))
                continue;

            _registryBuffer[surface.PlanetType.Id] = (uid, comp);
        }
    }

    /// <summary>The world's vein rating, or false when its surface rolls no veins at all.</summary>
    private bool TryRate(WFSectorPlanetComponent comp, bool sanctioned, out WFVeinRating rating)
    {
        rating = WFVeinRating.Poor;

        if (comp.Surface is not { } surfaceId || !_proto.TryIndex(surfaceId, out var surface))
            return false;

        if (surface.Veins is not { } tableId || !_proto.TryIndex(tableId, out var table))
            return false;

        if (!_proto.TryIndex<WFVeinRatingBandsPrototype>(SharedWFSurveySystem.DefaultBands, out var bands))
            return false;

        rating = SharedWFSurveySystem.Rate(SharedWFSurveySystem.Score(table, sanctioned), bands);
        return true;
    }

    /// <summary>Reconciles the screen sprite of every survey console in the world against its own listener state.</summary>
    private void UpdateScreens()
    {
        var query = EntityQueryEnumerator<WFSectorSurveyConsoleComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            var screen = _listeners.Contains(uid) ? WFSurveyConsoleScreen.Scanning : WFSurveyConsoleScreen.Idle;
            _appearance.SetData(uid, WFSurveyConsoleVisuals.Screen, screen);
        }
    }
}
