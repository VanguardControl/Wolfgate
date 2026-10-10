using Content.Server._WF.ShipPa;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Shared._WF.SectorControl;
using Content.Shared.GameTicking;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.SectorControl.Systems;

/// <summary>Tells those aboard a ship over its PA, or in chat, when it has been in or out of a faction's space for a minute.</summary>
public sealed partial class WFSectorNoticeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private ShipPaSystem _shipPa = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;
    [Dependency] private WFSectorActivitySystem _activity = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long a ship stays inside or outside before it is told.</summary>
    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(60);

    /// <summary>The least time between two notices to one ship.</summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    private readonly Dictionary<EntityUid, NoticeState> _ships = new();
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _ships.Clear();
        _nextCheck = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_territory.Enabled || _ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        Check();
    }

    /// <summary>One pass over the crewed ships of the sector map, as the timer runs it.</summary>
    public void Check()
    {
        var now = _timing.CurTime;
        var map = _territory.SectorMap;
        var ships = _activity.CrewedShips(map);
        var gone = new List<EntityUid>();
        foreach (var grid in _ships.Keys)
        {
            if (!ships.Contains(grid))
                gone.Add(grid);
        }

        foreach (var grid in gone)
        {
            _ships.Remove(grid);
        }

        foreach (var grid in ships)
        {
            var cell = _territory.CellOf(_transform.GetMapCoordinates(grid));
            var owner = _territory.Owner(map, cell);
            if (!_ships.TryGetValue(grid, out var state))
            {
                state = new NoticeState { Since = now };
                _ships[grid] = state;
            }

            if (owner != state.Current)
            {
                state.Current = owner;
                state.Since = now;
            }

            if (state.Current == state.Announced || now - state.Since < Dwell || now < state.NextAllowed)
                continue;

            Notify(grid, state.Announced, state.Current, cell);
            state.Announced = state.Current;
            state.NextAllowed = now + Cooldown;
        }
    }

    private void Notify(EntityUid grid, ProtoId<WFSectorFactionPrototype>? left, ProtoId<WFSectorFactionPrototype>? entered, WFSectorCell cell)
    {
        var shown = entered ?? left;
        if (shown == null || !_prototypes.TryIndex(shown.Value, out var faction))
            return;

        var message = Loc.GetString(entered != null ? "sector-control-notice-entered" : "sector-control-notice-left",
            ("ship", Name(grid)),
            ("faction", Loc.GetString(faction.Name)),
            ("callsign", WFSectorHex.Callsign(cell)));
        var sender = Loc.GetString("sector-control-notice-sender");
        if (_shipPa.Announce(grid, message, sender: sender, color: faction.Color))
            return;

        var aboard = Filter.Empty();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out _, out var actor, out var xform))
        {
            if (xform.GridUid == grid)
                aboard.AddPlayer(actor.PlayerSession);
        }

        _chat.DispatchFilteredAnnouncement(aboard, message, grid, sender, playSound: false, colorOverride: faction.Color);
    }

    /// <summary>Whose space a ship is in, since when, and what it was last told.</summary>
    private sealed class NoticeState
    {
        public ProtoId<WFSectorFactionPrototype>? Current;
        public ProtoId<WFSectorFactionPrototype>? Announced;
        public TimeSpan Since;
        public TimeSpan NextAllowed;
    }
}
