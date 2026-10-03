using System.Globalization;
using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Chat.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Radio;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>What the radio officer says; each line is a Fluent message with numbered variants.</summary>
public enum WFRadioLine : byte
{
    Docking,
    Undocking,
    Jumping,
    Arriving,
    OnStation,
    DockAborted,
    Mayday,
    Boarded,
    CaptainDown,
    HelmDown,
    AllClear,
    Approach,
    DockWarning,
    BoardWarning,
}

/// <summary>One line the radio officer put on the air.</summary>
public readonly record struct WFRadioTransmission(
    WFRadioLine Line,
    ProtoId<RadioChannelPrototype> Channel,
    string Text);

/// <summary>
/// Radio officers report what happens to their ship: routine traffic on the local channel, attacks and losses on the
/// alert channel. Driven by ship and crew events plus a once-a-second look for boarders, so it works while the
/// officer's HTN sleeps. He transmits as himself and only while he is up, so downing him ends the broadcasts.
/// </summary>
public sealed class WFRadioOperatorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILocalizationManager _loc = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCrewSecuritySystem _security = default!;

    /// <summary>How many transmissions <see cref="WFRadioOperatorComponent.Sent"/> keeps.</summary>
    private const int SentKept = 20;

    /// <summary>Mayday coordinates are rounded to this many metres.</summary>
    private const float PositionRounding = 10f;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextPoll;

    // DockingSystem.GetDocks reuses a set that UndockDocks is iterating when it raises UndockEvent.
    private readonly HashSet<Entity<DockingComponent>> _docks = new();

    private readonly List<(Entity<WFRadioOperatorComponent> Op, EntityUid Grid)> _polled = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCrewComponent, BeforeDamageChangedEvent>(OnCrewDamaged,
            before: [typeof(Content.Shared._Onyx.Wounds.WoundDamageRoutingSystem)]);
        SubscribeLocalEvent<DockEvent>(OnDock);
        SubscribeLocalEvent<UndockEvent>(OnUndock);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFtlCompleted);
        SubscribeLocalEvent<WFPilotOrdersCompletedEvent>(OnOrdersCompleted);
        SubscribeLocalEvent<WFPilotDockFailedEvent>(OnDockFailed);
        SubscribeLocalEvent<WFCrewMemberDownEvent>(OnCrewDown);
        SubscribeLocalEvent<WFCrewHullHitEvent>(OnHullHit);
        SubscribeLocalEvent<WFCrewAlertEvent>(OnCrewAlert);
        SubscribeLocalEvent<WFCrewSecurityIncidentEvent>(OnSecurityIncident);
        SubscribeLocalEvent<WFPilotOrdersChangedEvent>(OnOrdersChanged);
    }

    /// <summary>Once a second: boarders, the drive spooling up, and the all-clear.</summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextPoll)
            return;

        _nextPoll = now + PollInterval;

        _polled.Clear();
        var operators = EntityQueryEnumerator<WFRadioOperatorComponent, TransformComponent>();
        while (operators.MoveNext(out var uid, out var radio, out var xform))
        {
            if (xform.GridUid is { } grid)
                _polled.Add(((uid, radio), grid));
        }

        foreach (var (op, grid) in _polled)
        {
            // From anywhere aboard, the grid's diagonal reaches all of it.
            var range = TryComp<MapGridComponent>(grid, out var gridComp) ? gridComp.LocalAABB.Size.Length() : 0f;
            foreach (var hostile in _faction.GetNearbyHostiles(op.Owner, range))
            {
                if (Transform(hostile).GridUid != grid
                    || !TryComp<MobStateComponent>(hostile, out var state)
                    || !_mobState.IsAlive(hostile, state)
                    || !(EntityManager.System<WFCrewWeaponSystem>().CanSee(op, hostile)
                         || EntityManager.System<WFCrewCommsSystem>().Knows(op, hostile))
                    || InCrew(hostile, GroupOf(op), grid))
                {
                    continue;
                }

                Boarded(op, hostile);
                break;
            }

            // Said while the drive spools: FTLStartedEvent comes once the ship is in FTL space, out of reach.
            var spooling = TryComp<FTLComponent>(grid, out var ftl) && ftl.State == FTLState.Starting;
            if (!spooling)
                op.Comp.JumpAnnounced = false;
            else if (!op.Comp.JumpAnnounced)
                op.Comp.JumpAnnounced = TrySend(op, WFRadioLine.Jumping);

            if (op.Comp.Alerted && now >= op.Comp.LastHostileActivity + op.Comp.AllClearDelay)
                AllClear(op);
        }
    }

    /// <summary>Sets what the ship is called on the air; null or blank goes back to the grid's name.</summary>
    public void SetCallsign(Entity<WFRadioOperatorComponent?> ent, string? callsign)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.Callsign = string.IsNullOrWhiteSpace(callsign) ? null : callsign.Trim();
    }

    /// <summary>
    /// A crewman hurt by someone outside the crew is a hostile act for the crew's radio officers. Before the damage
    /// lands, because Wolfmed routes a body's damage through its parts and the body's own DamageChangedEvent then has
    /// no origin.
    /// </summary>
    private void OnCrewDamaged(Entity<WFCrewComponent> ent, ref BeforeDamageChangedEvent args)
    {
        var (uid, component) = ent;
        if (args.Cancelled || !args.Damage.AnyPositive() || args.Origin is not { } origin || origin == uid)
            return;

        if (InCrew(origin, component.Group, Transform(uid).GridUid))
            return;

        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var witness, out var crew, out var xform))
        {
            if (!_mobState.IsAlive(witness) || !InCrew(uid, crew.Group, xform.GridUid)
                || witness != uid && !EntityManager.System<WFCrewWeaponSystem>().CanSee(witness, uid))
                continue;
            ReceiveIncident(witness, origin, WFRadioLine.Mayday);
            EntityManager.System<WFCrewCommsSystem>().Report(witness, origin, WFRadioLine.Mayday);
        }
    }

    private void OnDock(DockEvent args)
    {
        if (args.GridAUid == args.GridBUid)
            return;

        // Each dock is docked with the other side's port by now.
        GridDocked(args.GridAUid, args.GridBUid, args.DockA.DockedWith);
        GridDocked(args.GridBUid, args.GridAUid, args.DockB.DockedWith);
    }

    private void OnHullHit(ref WFCrewHullHitEvent args)
    {
        foreach (var op in OperatorsOn(args.Grid))
            HostileAct(op, args.AttackerGrid);
    }

    private void OnCrewAlert(ref WFCrewAlertEvent args)
    {
        if (args.Hostiles.Length == 0)
            return;
        foreach (var op in OperatorsOn(args.Grid))
        {
            if (GroupOf(op) == args.Group && (HasComp<MapGridComponent>(args.Hostiles[0])
                || EntityManager.System<WFCrewWeaponSystem>().CanSee(op, args.Hostiles[0])
                || EntityManager.System<WFCrewCommsSystem>().Knows(op, args.Hostiles[0])))
                HostileAct(op, args.Hostiles[0]);
        }
    }

    private void OnUndock(UndockEvent args)
    {
        if (args.GridAUid == args.GridBUid)
            return;

        GridUndocked(args.GridAUid, args.GridBUid);
        GridUndocked(args.GridBUid, args.GridAUid);
    }

    private void OnFtlCompleted(ref FTLCompletedEvent args)
    {
        foreach (var op in OperatorsOn(args.Entity))
        {
            TrySend(op, WFRadioLine.Arriving);
        }
    }

    private void OnOrdersCompleted(ref WFPilotOrdersCompletedEvent args)
    {
        foreach (var op in OperatorsOfCrew(args.Mob))
        {
            TrySend(op, WFRadioLine.OnStation);
        }
    }

    private void OnDockFailed(ref WFPilotDockFailedEvent args)
    {
        foreach (var op in OperatorsOfCrew(args.Mob))
        {
            TrySend(op, WFRadioLine.DockAborted);
        }
    }

    /// <summary>The captain or the pilot going down gets one line per crewman.</summary>
    private void OnCrewDown(ref WFCrewMemberDownEvent args)
    {
        WFRadioLine line;
        if (args.Role == WFCrewRoles.Captain)
            line = WFRadioLine.CaptainDown;
        else if (args.Role == WFCrewRoles.Pilot)
            line = WFRadioLine.HelmDown;
        else
            return;

        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var witness, out var crew, out var xform))
        {
            if (witness == args.Mob || !_mobState.IsAlive(witness)
                || !InCrew(args.Mob, crew.Group, xform.GridUid)
                || !EntityManager.System<WFCrewWeaponSystem>().CanSee(witness, args.Mob))
                continue;
            ReceiveIncident(witness, args.Mob, line);
            EntityManager.System<WFCrewCommsSystem>().Report(witness, args.Mob, line);
        }
    }

    /// <summary>Announces an incident personally witnessed or delivered through an equipped radio.</summary>
    public void ReceiveIncident(EntityUid recipient, EntityUid subject, WFRadioLine line)
    {
        if (!TryComp<WFRadioOperatorComponent>(recipient, out var radio) || !_mobState.IsAlive(recipient))
            return;
        if (line == WFRadioLine.Mayday)
        {
            HostileAct((recipient, radio), subject);
            return;
        }
        if (line is not (WFRadioLine.CaptainDown or WFRadioLine.HelmDown)
            || recipient == subject || radio.DownReported.Contains(subject))
            return;
        if (TrySend((recipient, radio), line))
            radio.DownReported.Add(subject);
    }

    /// <summary>Reports a docking, once per grid docked with however many ports connect.</summary>
    private void GridDocked(EntityUid grid, EntityUid other, EntityUid? port)
    {
        if (DocksBetween(grid, other) > 1)
            return;

        foreach (var op in OperatorsOn(grid))
        {
            if (!_security.IsOutboundDock(grid, GroupOf(op), other))
                continue;
            TrySend(op,
                WFRadioLine.Docking,
                ("station", NameOrUnknown(other)),
                ("port", port is { } dock ? Name(dock) : string.Empty));
        }
    }

    private void OnOrdersChanged(ref WFPilotOrdersChangedEvent args)
    {
        if (args.Orders != WFPilotOrder.Dock || !TryComp<WFPilotDutyComponent>(args.Mob, out var duty)
            || duty.DockTarget is not { } target)
            return;
        foreach (var op in OperatorsOfCrew(args.Mob))
            TrySend(op, WFRadioLine.Approach, ("station", NameOrUnknown(target)));
    }

    private void OnSecurityIncident(ref WFCrewSecurityIncidentEvent args)
    {
        foreach (var op in OperatorsOn(args.Grid))
        {
            if (GroupOf(op) != args.Group || !args.Docking
                && !EntityManager.System<WFCrewWeaponSystem>().CanSee(op, args.Visitor)
                && !EntityManager.System<WFCrewCommsSystem>().Knows(op, args.Visitor))
                continue;
            TrySend(op, args.Docking ? WFRadioLine.DockWarning : WFRadioLine.BoardWarning,
                ("visitor", NameOrUnknown(args.Visitor)));
            if (args.Response == WFCrewSecurityResponse.Hostile)
                HostileAct(op, args.Visitor);
        }
    }

    /// <summary>Reports leaving a grid once its last port lets go.</summary>
    private void GridUndocked(EntityUid grid, EntityUid other)
    {
        if (DocksBetween(grid, other) > 0)
            return;

        foreach (var op in OperatorsOn(grid))
        {
            TrySend(op, WFRadioLine.Undocking, ("station", NameOrUnknown(other)));
        }
    }

    /// <summary>Starts or extends the attack episode; its first act gets the mayday.</summary>
    private void HostileAct(Entity<WFRadioOperatorComponent> ent, EntityUid hostile)
    {
        ent.Comp.Alerted = true;
        ent.Comp.LastHostileActivity = _timing.CurTime;
        if (ent.Comp.MaydaySent || Transform(ent).GridUid is not { } grid)
            return;

        var vessel = string.Empty;
        if (TryComp<TransformComponent>(hostile, out var hostileXform)
            && (HasComp<MapGridComponent>(hostile) ? hostile : hostileXform.GridUid) is { } hostileGrid
            && hostileGrid != grid
            && Name(hostileGrid) is { Length: > 0 } hostileName)
        {
            vessel = _loc.GetString("wf-crew-radio-mayday-vessel", ("hostile", hostileName));
        }

        var position = _transform.GetWorldPosition(grid);
        ent.Comp.MaydaySent = TrySend(ent,
            WFRadioLine.Mayday,
            ("x", Rounded(position.X)),
            ("y", Rounded(position.Y)),
            ("vessel", vessel));
    }

    private void Boarded(Entity<WFRadioOperatorComponent> ent, EntityUid boarder)
    {
        HostileAct(ent, boarder);
        if (!ent.Comp.BoardedSent)
            ent.Comp.BoardedSent = TrySend(ent, WFRadioLine.Boarded);
    }

    /// <summary>Ends the episode, with the all-clear if a mayday went out.</summary>
    private void AllClear(Entity<WFRadioOperatorComponent> ent)
    {
        if (ent.Comp.MaydaySent)
            TrySend(ent, WFRadioLine.AllClear);

        ent.Comp.Alerted = false;
        ent.Comp.MaydaySent = false;
        ent.Comp.BoardedSent = false;
    }

    /// <summary>
    /// Puts a random variant of a line on the air, with the callsign added to its arguments. False when the officer
    /// is down or off any grid, or said the same line within the cooldown.
    /// </summary>
    private bool TrySend(Entity<WFRadioOperatorComponent> ent, WFRadioLine line, params (string, object)[] args)
    {
        var (uid, radio) = ent;
        if (TerminatingOrDeleted(uid)
            || _mobState.IsIncapacitated(uid)
            || Transform(uid).GridUid is not { } grid
            || TerminatingOrDeleted(grid))
        {
            return false;
        }

        var now = _timing.CurTime;
        if (radio.LastSent.TryGetValue(line, out var last) && now < last + radio.Cooldown)
            return false;

        var id = $"wf-crew-radio-{LineId(line)}";
        var variants = 0;
        while (_loc.HasString($"{id}-{variants + 1}"))
        {
            variants++;
        }

        if (variants == 0)
        {
            Log.Error($"No Fluent variants for {id}");
            return false;
        }

        var callsign = string.IsNullOrWhiteSpace(radio.Callsign) ? NameOrUnknown(grid) : radio.Callsign;
        var text = _loc.GetString($"{id}-{_random.Next(1, variants + 1)}", args.Append(("callsign", callsign)).ToArray());
        radio.LastSent[line] = now;

        if (IsAlert(line))
        {
            Transmit(ent, line, radio.AlertChannel, text);
            if (radio.LocalChannel != radio.AlertChannel)
                Transmit(ent, line, radio.LocalChannel, text);
            if (radio.FactionChannel is { } faction && faction != radio.AlertChannel && faction != radio.LocalChannel)
                Transmit(ent, line, faction, text);
        }
        else
        {
            Transmit(ent, line, radio.LocalChannel, text);
        }

        if (radio.SpeakAloud)
            _chat.TrySendInGameICMessage(uid, text, InGameICChatType.Speak, hideChat: false, checkRadioPrefix: false);

        return true;
    }

    private void Transmit(Entity<WFRadioOperatorComponent> ent,
        WFRadioLine line,
        ProtoId<RadioChannelPrototype> channel,
        string text)
    {
        _radio.SendRadioMessage(ent, text, channel, ent);
        ent.Comp.Sent.Add(new WFRadioTransmission(line, channel, text));
        if (ent.Comp.Sent.Count > SentKept)
            ent.Comp.Sent.RemoveAt(0);
    }

    /// <summary>
    /// Whether the entity belongs to this grid's crew group.
    /// </summary>
    private bool InCrew(EntityUid uid, string group, EntityUid? grid)
    {
        return TryComp<WFCrewComponent>(uid, out var crew)
               && crew.Group == group
               && Transform(uid).GridUid == grid;
    }

    private string GroupOf(EntityUid op)
    {
        return TryComp<WFCrewComponent>(op, out var crew) ? crew.Group : string.Empty;
    }

    /// <summary>Radio officers of the crewman's crew.</summary>
    private List<Entity<WFRadioOperatorComponent>> OperatorsOfCrew(EntityUid crewman)
    {
        var found = new List<Entity<WFRadioOperatorComponent>>();
        var query = EntityQueryEnumerator<WFRadioOperatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var radio, out var xform))
        {
            if (InCrew(crewman, GroupOf(uid), xform.GridUid))
                found.Add((uid, radio));
        }

        return found;
    }

    /// <summary>Radio officers aboard the grid.</summary>
    private List<Entity<WFRadioOperatorComponent>> OperatorsOn(EntityUid grid)
    {
        var found = new List<Entity<WFRadioOperatorComponent>>();
        var query = EntityQueryEnumerator<WFRadioOperatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var radio, out var xform))
        {
            if (xform.GridUid == grid)
                found.Add((uid, radio));
        }

        return found;
    }

    /// <summary>Ports on the grid docked with ports on the other grid.</summary>
    private int DocksBetween(EntityUid grid, EntityUid other)
    {
        _docks.Clear();
        _lookup.GetChildEntities(grid, _docks);
        var count = 0;
        foreach (var dock in _docks)
        {
            if (dock.Comp.DockedWith is { } with && Transform(with).GridUid == other)
                count++;
        }

        return count;
    }

    private string NameOrUnknown(EntityUid uid)
    {
        return Name(uid) is { Length: > 0 } name ? name : _loc.GetString("wf-crew-radio-unnamed");
    }

    private static string Rounded(float value)
    {
        return ((int) MathF.Round(value / PositionRounding) * (int) PositionRounding).ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsAlert(WFRadioLine line)
    {
        return line is WFRadioLine.Mayday
            or WFRadioLine.Boarded
            or WFRadioLine.CaptainDown
            or WFRadioLine.HelmDown
            or WFRadioLine.BoardWarning
            or WFRadioLine.DockWarning
            or WFRadioLine.AllClear;
    }

    private static string LineId(WFRadioLine line)
    {
        return line switch
        {
            WFRadioLine.Docking => "docking",
            WFRadioLine.Undocking => "undocking",
            WFRadioLine.Jumping => "jumping",
            WFRadioLine.Arriving => "arriving",
            WFRadioLine.OnStation => "on-station",
            WFRadioLine.DockAborted => "dock-aborted",
            WFRadioLine.Mayday => "mayday",
            WFRadioLine.Boarded => "boarded",
            WFRadioLine.CaptainDown => "captain-down",
            WFRadioLine.HelmDown => "helm-down",
            WFRadioLine.AllClear => "all-clear",
            WFRadioLine.Approach => "approach",
            WFRadioLine.DockWarning => "dock-warning",
            WFRadioLine.BoardWarning => "board-warning",
            _ => throw new ArgumentOutOfRangeException(nameof(line), line, null),
        };
    }
}
