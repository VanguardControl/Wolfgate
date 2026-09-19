using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Cracker.BUI;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>
/// Server half of the crack control console: it authors the whole window state and turns its three messages into
/// targeting, untargeting and the begin.
/// Everything the window draws is built here (design D-N): grid membership is not a PVS exemption, the berth marker
/// sits 26 tiles past itself on a MarkerBase prototype and berth-side projectors on a capital hull are routinely past
/// net.pvs_range, so a client that re-derived the geometry would draw an empty diagram while the server read nominal.
/// Open consoles are tracked in a set pruned by polling the UI rather than by claiming BoundUIClosedEvent, so nothing
/// here competes for a subscription another system may already own.
/// </summary>
public sealed partial class WFCrackConsoleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPowerReceiverSystem _receiver = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WFCrackerSystem _crackers = default!;

    /// <summary>Push interval while nothing is cutting.</summary>
    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(1);

    /// <summary>Push interval while a listening console's hull is cutting, so its timers read live.</summary>
    private static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>Consoles with the window open; pruned by polling the UI each sweep.</summary>
    private readonly HashSet<EntityUid> _listeners = new();

    /// <summary>Listeners that have gone away, collected before removal so the set is not mutated while read.</summary>
    private readonly List<EntityUid> _stale = new();

    /// <summary>Projectors on one hull, rebuilt per state build.</summary>
    private readonly List<Entity<WFGravityProjectorComponent>> _projectorBuffer = new();

    /// <summary>Next push.</summary>
    private TimeSpan _nextUpdate;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // Unclaimed: nothing else in the codebase subscribes a directed event on WFCrackConsoleComponent.
        SubscribeLocalEvent<WFCrackConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);

        // Broadcast and by ref, matching the [ByRefEvent] the cracker's SetState raises.
        SubscribeLocalEvent<WFCrackStateChangedEvent>(OnCrackStateChanged);

        // Auto-filters on args.UiKey, so the three handlers never see another interface's traffic.
        Subs.BuiEvents<WFCrackConsoleComponent>(WFCrackConsoleUiKey.Key, subs =>
        {
            subs.Event<WFCrackTargetMessage>(OnTargetMessage);
            subs.Event<WFCrackUntargetMessage>(OnUntargetMessage);
            subs.Event<WFCrackBeginMessage>(OnBeginMessage);
        });
    }

    /// <summary>A freshly opened window starts listening and gets its first state at once rather than up to a second late.</summary>
    private void OnUiOpened(EntityUid uid, WFCrackConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (!Equals(args.UiKey, WFCrackConsoleUiKey.Key))
            return;

        _listeners.Add(uid);
        PushState(uid);
    }

    /// <summary>Every stage change is pushed straight to the hull's consoles, screen sprite included.</summary>
    private void OnCrackStateChanged(ref WFCrackStateChangedEvent args)
    {
        var query = EntityQueryEnumerator<WFCrackConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != args.Cracker)
                continue;

            UpdateScreen(uid);

            if (_listeners.Contains(uid))
                PushState(uid);
        }
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        PruneListeners();

        var fast = false;

        foreach (var console in _listeners)
        {
            var state = BuildState(console);
            _ui.SetUiState(console, WFCrackConsoleUiKey.Key, state);

            fast |= state.State == WFCrackState.Cracking;
        }

        // Cheap even with no window open: SetData early-returns when the value has not moved, and this is what gives a
        // console its first screen face, since the map-init reset to Idle is a no-op that raises no state-changed event.
        UpdateScreens();

        _nextUpdate = _timing.CurTime + (fast ? FastInterval : SlowInterval);
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
            if (!Exists(console) || !_ui.IsUiOpen(console, WFCrackConsoleUiKey.Key))
                _stale.Add(console);
        }

        foreach (var console in _stale)
        {
            _listeners.Remove(console);
        }
    }

    /// <summary>Pushes one console's state, whether or not it is in the listener set.</summary>
    private void PushState(EntityUid console)
    {
        _ui.SetUiState(console, WFCrackConsoleUiKey.Key, BuildState(console));
    }

    /// <summary>
    /// Everything the window draws, resolved off the hull the console rests on.
    /// Public so a test may read exactly what a client would receive without standing a window up.
    /// </summary>
    public WFCrackConsoleState BuildState(EntityUid console)
    {
        var state = new WFCrackConsoleState();

        if (!TryGetCracker(console, out var cracker))
            return state;

        var comp = cracker.Comp;

        state.State = comp.State;
        state.Cracker = GetNetEntity(cracker.Owner);
        state.AnchorA = comp.AnchorA;
        state.AnchorB = comp.AnchorB;
        state.AlignTolerance = comp.AlignTolerance;
        state.PendingAbort = comp.PendingAbort;
        state.CrackPaused = comp.CrackPaused;
        state.CrackTotal = comp.CrackDuration;
        state.GraceRunning = comp.GraceRunning;
        state.Failing = comp.Failing;

        // The timers are recomputed from the deadlines rather than read off the banked fields, so a console pushed
        // between two sweeps still counts down smoothly. A paused cut has no live deadline, so it reads the bank.
        state.CrackRemaining = comp.State == WFCrackState.Cracking && !comp.CrackPaused
            ? Remaining(comp.CrackEnd)
            : comp.CrackRemaining;
        state.GraceRemaining = comp.GraceRunning ? Remaining(comp.GraceEnd) : TimeSpan.Zero;
        state.AbortRemaining = comp.PendingAbort is null ? TimeSpan.Zero : Remaining(comp.AbortEnd);

        // Same convention as the timers above: the two disconnect countdowns are derived from their deadlines, not
        // from a banked field, so a console pushed between two sweeps still counts down smoothly.
        state.DisconnectArmed = comp.DisconnectArmed;
        state.DisconnectRemaining = comp.DisconnectArmed ? Remaining(comp.DisconnectEnd) : TimeSpan.Zero;
        state.EvacRunning = comp.EvacRunning;
        state.EvacRemaining = comp.EvacRunning ? Remaining(comp.EvacEnd) : TimeSpan.Zero;

        // The owned pair is what the target button would act on. Not locked-only: a pair still drilling is what the
        // diagram should be drawing, and the button's own preconditions are the blocker flags below.
        var owned = _crackers.TryGetOwnedPair(cracker, out var ownedA, out var ownedB, false);

        if (owned)
        {
            state.CandidateA = GetNetEntity(ownedA.Owner);
            state.CandidateB = GetNetEntity(ownedB.Owner);
        }

        // The shown pair is the targeted one where there is one, so the cut stays drawn even after the pair stops
        // qualifying as a candidate.
        var shown = _crackers.TryGetTargetedPair(cracker, out var a, out var b);

        if (!shown && owned)
        {
            a = ownedA;
            b = ownedB;
            shown = true;
        }

        if (shown)
        {
            state.AnchorAPos = _transform.GetWorldPosition(a.Owner);
            state.AnchorBPos = _transform.GetWorldPosition(b.Owner);
            state.AnchorAState = a.Comp.State;
            state.AnchorBState = b.Comp.State;
            state.AnchorADamaged = a.Comp.Damaged;
            state.AnchorBDamaged = b.Comp.Damaged;

            if (_crackers.TryGetCircle(a.Owner, b.Owner, out var circleCentre, out var radius))
            {
                state.CircleCentre = circleCentre;
                state.CircleRadius = radius;
            }

            if (_crackers.TryGetBerthOffset(cracker, a.Owner, b.Owner, out var offset))
            {
                state.BerthOffset = offset;
                state.Aligned = offset.Length() <= comp.AlignTolerance;
            }
        }

        // One derivation, shared with the radar ghost and F5's chunk placement.
        if (_crackers.TryGetBerthRect(cracker, out var rect))
        {
            state.BerthCentre = rect.Center;
            state.BerthHalfExtents = rect.Box.Size / 2f;
            state.BerthRotation = rect.Rotation;
        }

        if (TryComp<MapGridComponent>(cracker.Owner, out var grid))
        {
            // The pose travels with the AABB: without it the diagram can only draw the hull concentric with the berth,
            // which is 26 tiles off on the shipped marker and misstates the one relationship the plan view is for.
            state.HullAabb = grid.LocalAABB;
            (state.HullPos, state.HullRotation) = _transform.GetWorldPositionRotation(cracker.Owner);
        }

        if (_crackers.TryGetCentrifuge(cracker.Owner, out var centrifuge))
        {
            state.Spin = centrifuge.Comp.Spin;
            state.AtFull = centrifuge.Comp.AtFull;
            state.Load = centrifuge.Comp.Load;
            state.Capacity = centrifuge.Comp.Capacity;
        }

        _crackers.GetProjectors(cracker.Owner, _projectorBuffer);

        foreach (var projector in _projectorBuffer)
        {
            state.Projectors.Add(new WFProjectorRow(
                Transform(projector.Owner).LocalPosition,
                projector.Comp.State,
                _receiver.IsPowered(projector.Owner),
                projector.Comp.Broken));
        }

        var blockers = _crackers.ComputeBlockers(cracker);
        state.Blockers = blockers;

        // Targeting cares about everything except having a target already; untargeting is legal only where the target
        // may legally be dropped (design D23); begin needs the whole list clear.
        const WFCrackBlocker targeting = WFCrackBlocker.WrongState
            | WFCrackBlocker.NoPair
            | WFCrackBlocker.NotAligned
            | WFCrackBlocker.Obstructed;

        state.CanTarget = comp.AnchorA is null && (blockers & targeting) == WFCrackBlocker.None;
        state.CanUntarget = comp.State == WFCrackState.AnchorsLocked && comp.AnchorA is not null;
        state.CanBegin = blockers == WFCrackBlocker.None;

        return state;
    }

    /// <summary>Targets the hull's pair, or tells the user why it will not.</summary>
    private void OnTargetMessage(EntityUid uid, WFCrackConsoleComponent component, WFCrackTargetMessage args)
    {
        if (!TryGetCracker(uid, out var cracker))
            return;

        if (!_crackers.TryTarget(cracker, out var reason))
            Refuse(uid, args.Actor, reason);

        PushState(uid);
    }

    /// <summary>Drops the target, which is refused once the cut has begun (design D23).</summary>
    private void OnUntargetMessage(EntityUid uid, WFCrackConsoleComponent component, WFCrackUntargetMessage args)
    {
        if (!TryGetCracker(uid, out var cracker))
            return;

        if (!_crackers.TryUntarget(cracker, out var reason))
            Refuse(uid, args.Actor, reason);

        PushState(uid);
    }

    /// <summary>Begins the cut. There is deliberately no abort counterpart.</summary>
    private void OnBeginMessage(EntityUid uid, WFCrackConsoleComponent component, WFCrackBeginMessage args)
    {
        if (!TryGetCracker(uid, out var cracker))
            return;

        if (!_crackers.TryBegin(cracker, out var reason))
            Refuse(uid, args.Actor, reason);

        PushState(uid);
    }

    /// <summary>Shows the refusal at the console; the reason is always a locale key, never a server-authored string.</summary>
    private void Refuse(EntityUid console, EntityUid actor, string? reason)
    {
        if (reason is null)
            return;

        _popup.PopupEntity(Loc.GetString(reason), console, actor);
    }

    /// <summary>Reconciles the screen sprite of every crack console in the world against its own hull.</summary>
    private void UpdateScreens()
    {
        var query = EntityQueryEnumerator<WFCrackConsoleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            UpdateScreen(uid);
        }
    }

    /// <summary>Pushes one console's screen face; a console on no cracker shows the idle face.</summary>
    private void UpdateScreen(EntityUid console)
    {
        var screen = TryGetCracker(console, out var cracker)
            ? GetScreen(cracker.Comp)
            : WFCrackConsoleScreen.Idle;

        _appearance.SetData(console, WFCrackConsoleVisuals.Screen, screen);
    }

    /// <summary>The four screen faces crack_console.rsi ships, mapped from the nine crack stages.</summary>
    public static WFCrackConsoleScreen GetScreen(WFPlanetCrackerComponent comp)
    {
        switch (comp.State)
        {
            case WFCrackState.AnchorsPlaced:
            case WFCrackState.AnchorsLocked:
                return WFCrackConsoleScreen.Targeting;

            // Anything failing or spinning down is an alert even mid-cut: that is what the crew has to react to.
            case WFCrackState.Cracking:
                return comp.GraceRunning || comp.PendingAbort is not null
                    ? WFCrackConsoleScreen.Alert
                    : WFCrackConsoleScreen.Cracking;

            case WFCrackState.Cracked:
            case WFCrackState.Disconnecting:
            case WFCrackState.Released:
            case WFCrackState.Falling:
                return WFCrackConsoleScreen.Alert;

            default:
                return WFCrackConsoleScreen.Idle;
        }
    }

    /// <summary>The cracker hull a console rests on, if it rests on one at all.</summary>
    public bool TryGetCracker(EntityUid console, out Entity<WFPlanetCrackerComponent> cracker)
    {
        cracker = default;

        if (Transform(console).GridUid is not { } grid || !TryComp<WFPlanetCrackerComponent>(grid, out var comp))
            return false;

        cracker = (grid, comp);
        return true;
    }

    /// <summary>Time left to a deadline, never negative.</summary>
    private TimeSpan Remaining(TimeSpan deadline)
    {
        var remaining = deadline - _timing.CurTime;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}
