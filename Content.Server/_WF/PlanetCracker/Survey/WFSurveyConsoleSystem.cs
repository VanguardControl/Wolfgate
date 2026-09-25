using System.Numerics;
using Content.Server._WF.Planets;
using Content.Server.Shuttles.Components;
using Content.Server.Warps;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.PlanetCracker;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared._WF.PlanetCracker.Survey.BUI;
using Content.Shared._WF.Planets;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Survey;

/// <summary>Server half of the read-only sector survey console: the window state and the screen face.</summary>
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

        // Also gives a console its first screen face; SetData skips unchanged values, so this stays cheap.
        UpdateScreens();

        _nextUpdate = _timing.CurTime + PushInterval;
    }

    /// <summary>Drops consoles whose window closed; polled so BoundUIClosedEvent stays free.</summary>
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

    /// <summary>Everything the window draws, in one server-side pass.</summary>
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
            // Must match SharedStarSystemMapSystem's spawn position expression exactly.
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
                cracked = HasComp<WFPlanetCrackedComponent>(body.Owner);
                veinsKnown = TryRate(body.Comp, sanctioned, out veins);
            }
            else if (_bodyBuffer.TryGetValue(name, out var unregistered))
            {
                // An unregistered body is still a beacon; Planet stays null, meaning "not registered".
                hasBeacon = HasComp<FTLBeaconComponent>(unregistered);
            }

            // Raw subtraction, not MapCoordinates.InRange, which fails across maps.
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

    /// <summary>The console's position in the sector frame; false on maps with no meaningful sector position.</summary>
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

        // The grid's position where the console rests on one, the console's otherwise.
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

    /// <summary>Indexes every sector body (a warp point parented to the map) by name, registered or not.</summary>
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

    /// <summary>Indexes every registered sector body by its surface's planet type, without allocating.</summary>
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

        if (comp.Surface is not { } surfaceId || !WFCrackSitePrototype.TryGet(_proto, surfaceId, out var site))
            return false;

        if (site.Veins is not { } tableId || !_proto.TryIndex(tableId, out var table))
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
