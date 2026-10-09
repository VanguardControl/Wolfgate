using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Destructible;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.Shuttles;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Shuttles.Systems;

/// <summary>
/// Feeds hull telemetry to shuttle consoles whose whole-ship view is on screen: structure condition,
/// fires, pressure and dead power. Only consoles that ask get a sweep, and only tiles with something
/// to report are sent.
/// </summary>
public sealed partial class ShipStatusSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private DestructibleSystem _destructible = default!;

    /// <summary>
    /// How often a listening console is refreshed. Fast enough to watch a fire spread, slow enough
    /// that the sweep doesn't matter.
    /// </summary>
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Ceiling on reported tiles per ship, so a catastrophically wrecked capital can't flood the wire.
    /// </summary>
    private const int MaxReportedTiles = 4000;

    /// <summary>
    /// Structures above this condition aren't worth drawing.
    /// </summary>
    private const float DamageReportThreshold = 0.98f;

    private const float MinNominalPressure = 80f;
    private const float MaxNominalPressure = 130f;

    /// <summary>
    /// Below this a tile counts as vented for the summary readout.
    /// </summary>
    private const float VentedPressure = 20f;

    /// <summary>
    /// Consoles currently showing the view, and which overlays each is drawing.
    /// </summary>
    private readonly Dictionary<EntityUid, ShipOverlays> _listeners = new();

    /// <summary>
    /// Rebuilt each sweep: the tiles being reported, keyed by grid then tile.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, ShipTileStatus>> _gridTiles = new();

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridSplitEvent>(OnHullSplit);

        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<ShipStatusRequestMessage>(OnStatusRequest);
        });
    }

    private void OnStatusRequest(Entity<ShuttleConsoleComponent> ent, ref ShipStatusRequestMessage args)
    {
        if (!args.Active)
        {
            _listeners.Remove(ent);
            return;
        }

        _listeners[ent] = args.Overlays;

        // The client only sends this on a change, so answering every time keeps switching to the tab
        // or flipping an overlay from leaving a second of blank screen.
        SweepAndSend(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;
        ForgetDeletedHulls();

        // ShuttleConsoleSystem already owns the BoundUIClosedEvent subscription for these consoles, and
        // Robust only allows one per component, so drop stale listeners by checking the UI instead.
        foreach (var console in _listeners.Keys.ToArray())
        {
            if (!Exists(console) || !_ui.IsUiOpen(console, ShuttleConsoleUiKey.Key))
                _listeners.Remove(console);
        }

        if (_listeners.Count == 0)
            return;

        // One sweep covers every listening console, so having several open costs no more than one.
        var grids = new Dictionary<EntityUid, List<EntityUid>>();

        foreach (var console in _listeners.Keys)
        {
            if (GetConsoleGrid(console) is not { } grid)
                continue;

            grids.GetOrNew(grid).Add(console);
        }

        if (grids.Count == 0)
            return;

        GatherTiles(grids.Keys.ToHashSet());

        foreach (var (grid, consoles) in grids)
        {
            foreach (var console in consoles)
            {
                _ui.ServerSendUiMessage(console,
                    ShuttleConsoleUiKey.Key,
                    BuildMessage(grid, _listeners.GetValueOrDefault(console, ShipOverlays.All)));
            }
        }
    }

    /// <summary>
    /// Sweeps the hull this console flies and returns what it found. Null if the console isn't on a grid.
    /// </summary>
    public ShipStatusMessage? GetStatus(EntityUid console, ShipOverlays overlays = ShipOverlays.All)
    {
        if (GetConsoleGrid(console) is not { } grid)
            return null;

        GatherTiles(new HashSet<EntityUid> { grid });
        return BuildMessage(grid, overlays);
    }

    private void SweepAndSend(EntityUid console)
    {
        if (GetStatus(console, _listeners.GetValueOrDefault(console, ShipOverlays.All)) is not { } message)
            return;

        _ui.ServerSendUiMessage(console, ShuttleConsoleUiKey.Key, message);
    }

    /// <summary>
    /// Resolves what the console is actually flying, which is not its own grid for drone consoles.
    /// </summary>
    private EntityUid? GetConsoleGrid(EntityUid console)
    {
        var ev = new ConsoleShuttleEvent { Console = console };
        RaiseLocalEvent(console, ref ev);

        if (ev.Console is not { } target || !TryComp(target, out TransformComponent? xform))
            return null;

        return xform.GridUid;
    }

    private void GatherTiles(HashSet<EntityUid> grids)
    {
        _gridTiles.Clear();
        _hullStructures.Clear();
        _hullIntegrity.Clear();

        foreach (var grid in grids)
        {
            _gridTiles[grid] = new Dictionary<Vector2i, ShipTileStatus>();
            _hullStructures[grid] = new Dictionary<Vector2i, (float Capacity, float Remaining)>();
        }

        GatherDamage(grids);
        GatherHullIntegrity(grids);
        GatherPower(grids);
        GatherAtmos(grids);
    }

    private void GatherDamage(HashSet<EntityUid> grids)
    {
        var query = EntityQueryEnumerator<DestructibleComponent, DamageableComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var destructible, out var damageable, out var xform))
        {
            if (!xform.Anchored || xform.GridUid is not { } grid || !grids.Contains(grid))
                continue;

            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid))
                continue;

            var destroyedAt = _destructible.DestroyedAt(uid, destructible);

            // Entities with no destruction threshold have no meaningful condition to report.
            if (destroyedAt <= 0 || destroyedAt == FixedPoint2.MaxValue)
                continue;

            if (!TryComp(grid, out MapGridComponent? gridComp))
                continue;

            var index = _maps.TileIndicesFor(grid, gridComp, xform.Coordinates);
            var capacity = destroyedAt.Float();
            var remaining = Math.Clamp(capacity - damageable.TotalDamage.Float(), 0f, capacity);
            var structure = _hullStructures[grid].GetValueOrDefault(index);
            _hullStructures[grid][index] = (structure.Capacity + capacity, structure.Remaining + remaining);
            var integrity = remaining / capacity;

            if (integrity > DamageReportThreshold)
                continue;

            var tiles = _gridTiles[grid];
            var status = tiles.GetValueOrDefault(index);

            // The worst structure on a tile is the one worth showing.
            if ((status.Flags & ShipTileFlags.Damaged) == 0 || integrity < status.Integrity / 255f)
                status.Integrity = (byte)(integrity * 255f);

            status.Index = index;
            status.Flags |= ShipTileFlags.Damaged;
            tiles[index] = status;
        }
    }

    private void GatherPower(HashSet<EntityUid> grids)
    {
        var query = EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();

        while (query.MoveNext(out _, out var receiver, out var xform))
        {
            if (receiver.Powered || !xform.Anchored)
                continue;

            if (xform.GridUid is not { } grid || !grids.Contains(grid))
                continue;

            if (!TryComp(grid, out MapGridComponent? gridComp))
                continue;

            var index = _maps.TileIndicesFor(grid, gridComp, xform.Coordinates);
            var tiles = _gridTiles[grid];
            var status = tiles.GetValueOrDefault(index);

            status.Index = index;
            status.Flags |= ShipTileFlags.Unpowered;
            tiles[index] = status;
        }
    }

    private void GatherAtmos(HashSet<EntityUid> grids)
    {
        foreach (var grid in grids)
        {
            if (!TryComp(grid, out GridAtmosphereComponent? atmos))
                continue;

            var tiles = _gridTiles[grid];

            foreach (var (index, tile) in atmos.Tiles)
            {
                // Tiles outside the hull aren't damage, they're just space.
                if (tile.Space || tile.Air == null)
                    continue;

                var pressure = tile.Air.Pressure;
                var onFire = tile.Hotspot.Valid;
                var offNominal = pressure < MinNominalPressure || pressure > MaxNominalPressure;

                if (!onFire && !offNominal)
                    continue;

                var status = tiles.GetValueOrDefault(index);
                status.Index = index;

                if (onFire)
                    status.Flags |= ShipTileFlags.Fire;

                if (offNominal)
                {
                    status.Flags |= ShipTileFlags.Pressure;
                    status.Pressure = (byte)Math.Clamp(pressure / 2f, 0f, 255f);
                }

                tiles[index] = status;
            }
        }
    }

    /// <summary>
    /// Counts everything for the summary, but only sends tiles for overlays the console is drawing.
    /// </summary>
    private ShipStatusMessage BuildMessage(EntityUid grid, ShipOverlays overlays)
    {
        var summary = new ShipStatusSummary { WorstIntegrity = 1f, HullIntegrity = _hullIntegrity.GetValueOrDefault(grid, 1f) };
        var list = new List<ShipTileStatus>();
        var wanted = (ShipTileFlags)overlays;

        if (!_gridTiles.TryGetValue(grid, out var tiles))
            return new ShipStatusMessage(GetNetEntity(grid), list, summary);

        foreach (var status in tiles.Values)
        {
            if ((status.Flags & ShipTileFlags.Damaged) != 0)
            {
                summary.DamagedTiles++;
                summary.WorstIntegrity = Math.Min(summary.WorstIntegrity, status.Integrity / 255f);
            }

            if ((status.Flags & ShipTileFlags.Fire) != 0)
                summary.FireTiles++;

            if ((status.Flags & ShipTileFlags.Unpowered) != 0)
                summary.UnpoweredTiles++;

            if ((status.Flags & ShipTileFlags.Pressure) != 0 && status.Pressure * 2f < VentedPressure)
                summary.VentedTiles++;

            var drawn = status.Flags & wanted;

            if (drawn == ShipTileFlags.None)
                continue;

            if (list.Count >= MaxReportedTiles)
            {
                summary.Truncated = true;
                continue;
            }

            var trimmed = status;
            trimmed.Flags = drawn;
            list.Add(trimmed);
        }

        return new ShipStatusMessage(GetNetEntity(grid), list, summary);
    }
}
