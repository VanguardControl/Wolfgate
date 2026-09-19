using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.PlanetCracker.Anchors;
using Content.Server._WF.PlanetCracker.Flight;
using Content.Server.Gravity;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>
/// The crack state machine: every transition of <see cref="WFPlanetCrackerComponent.State"/> other than the map-init
/// reset, driven by the six broadcast anchor events and by the one sweep in the Crack partial.
/// Only broadcast subscriptions are added here, all by ref to match the [ByRefEvent] anchor events; the hull's own
/// directed MapInitEvent pair stays the single one declared in WFCrackerSystem.cs.
/// </summary>
public sealed partial class WFCrackerSystem
{
    [Dependency] private CEZGridConnectorSystem _connectors = default!;
    [Dependency] private WFGravityAnchorSystem _anchors = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private DockingSystem _dock = default!;
    [Dependency] private GravityGeneratorSystem _gravgen = default!;
    [Dependency] private GravitySystem _gravity = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPowerReceiverSystem _receiver = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private WFGravityProjectorSystem _projectors = default!;
    [Dependency] private WFFlightSystem _flight = default!;

    /// <summary>Docked grids around the hull; reused by the snap and by the clear-area check.</summary>
    private readonly HashSet<EntityUid> _docked = new();

    /// <summary>Grids the clear-area check found over the destination footprint; not readonly, the query takes it by ref.</summary>
    private List<Entity<MapGridComponent>> _found = new();

    /// <summary>Docked grid poses relative to the hull, captured before the snap and reapplied after it.</summary>
    private readonly Dictionary<EntityUid, (Vector2 Position, Angle Rotation)> _dockedPoses = new();

    /// <summary>Projectors on one hull, rebuilt per call.</summary>
    private readonly List<Entity<WFGravityProjectorComponent>> _projectorBuffer = new();

    /// <summary>Registers the six anchor events; called from the system's one Initialize override.</summary>
    private void InitializeStateMachine()
    {
        // All six are broadcast [ByRefEvent] record structs raised by WFGravityAnchorSystem, so they are subscribed by
        // ref and add no directed (component, event) pair that another system could already own.
        SubscribeLocalEvent<WFAnchorPairFormedEvent>(OnPairFormed);
        SubscribeLocalEvent<WFAnchorPairDissolvedEvent>(OnPairDissolved);
        SubscribeLocalEvent<WFAnchorDrillFinishedEvent>(OnDrillFinished);
        SubscribeLocalEvent<WFAnchorDamagedEvent>(OnAnchorDamaged);
        SubscribeLocalEvent<WFAnchorBrokenEvent>(OnAnchorBroken);
        SubscribeLocalEvent<WFAnchorDestroyedEvent>(OnAnchorDestroyed);
    }

    /// <summary>A fresh pair on an owned anchor moves a surveying hull to anchors-placed.</summary>
    private void OnPairFormed(ref WFAnchorPairFormedEvent args)
    {
        if (!TryGetOwner(args.A, out var cracker) && !TryGetOwner(args.B, out cracker))
            return;

        if (cracker.Comp.State == WFCrackState.Surveying)
            SetState(cracker, WFCrackState.AnchorsPlaced);
    }

    /// <summary>A lost pair drops the hull back as far as its remaining anchors allow, and drops any target with it.</summary>
    private void OnPairDissolved(ref WFAnchorPairDissolvedEvent args)
    {
        // B is EntityUid.Invalid when the partner was already gone, so either half may be the one still resolvable.
        if (!TryGetOwner(args.A, out var cracker) && !TryGetOwner(args.B, out cracker))
            return;

        ReconcilePair(cracker);
    }

    /// <summary>Both halves locked is what makes a pair targetable.</summary>
    private void OnDrillFinished(ref WFAnchorDrillFinishedEvent args)
    {
        if (!TryGetOwner(args.Anchor, out var cracker))
            return;

        if (cracker.Comp.State != WFCrackState.AnchorsPlaced)
            return;

        if (!TryComp<WFGravityAnchorComponent>(args.Anchor, out var anchor) || anchor.State != WFAnchorState.Locked)
            return;

        if (!TryGetAnchor(anchor.Partner, out var partner) || partner.Comp.State != WFAnchorState.Locked)
            return;

        SetState(cracker, WFCrackState.AnchorsLocked);
    }

    /// <summary>
    /// A damaged targeted anchor holds the cut. The sweep re-checks the same condition, so a missed event cannot strand
    /// the timer either way; this handler only makes the reaction immediate.
    /// </summary>
    private void OnAnchorDamaged(ref WFAnchorDamagedEvent args)
    {
        if (!TryGetOwner(args.Anchor, out var cracker) || !IsTargeted(cracker, args.Anchor))
            return;

        UpdateCrackPause(cracker);
    }

    /// <summary>A broken targeted anchor is repairable, so the spin-down lands back on anchors-placed.</summary>
    private void OnAnchorBroken(ref WFAnchorBrokenEvent args)
    {
        if (!TryGetOwner(args.Anchor, out var cracker) || !IsTargeted(cracker, args.Anchor))
            return;

        StartAbort(cracker, WFCrackState.AnchorsPlaced);
    }

    /// <summary>A destroyed targeted anchor is gone for good, so the spin-down lands back on surveying.</summary>
    private void OnAnchorDestroyed(ref WFAnchorDestroyedEvent args)
    {
        if (!TryGetOwner(args.Anchor, out var cracker) || !IsTargeted(cracker, args.Anchor))
            return;

        StartAbort(cracker, WFCrackState.Surveying);
    }

    /// <summary>The idle-to-surveying edge and back: only a hull parked on a planet orbit layer can set a crack up.</summary>
    private void UpdateSurvey(Entity<WFPlanetCrackerComponent> ent)
    {
        var onOrbit = Transform(ent.Owner).MapUid is { } map && HasComp<WFOrbitLayerComponent>(map);

        switch (ent.Comp.State)
        {
            case WFCrackState.Idle when onOrbit:
                SetState(ent, WFCrackState.Surveying);
                break;
            case WFCrackState.Surveying when !onOrbit:
                ClearTarget(ent);
                SetState(ent, WFCrackState.Idle);
                break;
        }
    }

    /// <summary>
    /// Puts a setup-stage hull back where its anchors say it belongs. Surveying is included because a valid pair may
    /// already exist when the hull enters the orbit layer; in that case there is no new pair event to drive the edge.
    /// Once the cut is running the abort spin-down owns the fallback instead.
    /// </summary>
    private void ReconcilePair(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.State is not (WFCrackState.Surveying or WFCrackState.AnchorsPlaced or WFCrackState.AnchorsLocked))
            return;

        // A target whose pair has gone, stopped being a pair or stopped being locked is dropped before anything else.
        if (ent.Comp.AnchorA is not null
            && (!TryGetTargetedPair(ent, out var targetA, out var targetB)
                || targetA.Comp.State != WFAnchorState.Locked
                || targetB.Comp.State != WFAnchorState.Locked))
        {
            ClearTarget(ent);
        }

        if (!TryGetOwnedPair(ent, out var a, out var b, false))
        {
            ClearTarget(ent);
            SetState(ent, WFCrackState.Surveying);
            return;
        }

        if (a.Comp.State == WFAnchorState.Locked && b.Comp.State == WFAnchorState.Locked)
        {
            SetState(ent, WFCrackState.AnchorsLocked);
            return;
        }

        ClearTarget(ent);
        SetState(ent, WFCrackState.AnchorsPlaced);
    }

    /// <summary>Drops the targeted pair; the only writer of the two anchor fields other than targeting itself.</summary>
    public void ClearTarget(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.AnchorA is null && ent.Comp.AnchorB is null)
            return;

        ent.Comp.AnchorA = null;
        ent.Comp.AnchorB = null;
        Dirty(ent);
    }

    /// <summary>True when this anchor is one of the two the hull is cutting with.</summary>
    public bool IsTargeted(Entity<WFPlanetCrackerComponent> ent, EntityUid anchor)
    {
        var net = GetNetEntity(anchor);
        return ent.Comp.AnchorA == net || ent.Comp.AnchorB == net;
    }

    /// <summary>The cracker hull an anchor was stamped for; hand-spawned anchors with no owner belong to nobody.</summary>
    public bool TryGetOwner(EntityUid anchor, out Entity<WFPlanetCrackerComponent> cracker)
    {
        cracker = default;

        if (!TryComp<WFGravityAnchorComponent>(anchor, out var comp) || comp.Cracker is not { } net)
            return false;

        if (!TryGetEntity(net, out var uid) || !TryComp<WFPlanetCrackerComponent>(uid, out var crackerComp))
            return false;

        cracker = (uid.Value, crackerComp);
        return true;
    }

    /// <summary>Resolves a networked anchor reference that may point at something already gone.</summary>
    public bool TryGetAnchor(NetEntity? net, out Entity<WFGravityAnchorComponent> anchor)
    {
        anchor = default;

        if (net is not { } value || !TryGetEntity(value, out var uid))
            return false;

        if (!TryComp<WFGravityAnchorComponent>(uid, out var comp))
            return false;

        anchor = (uid.Value, comp);
        return true;
    }

    /// <summary>The pair the hull is cutting with; false as soon as either half is gone or they stopped being partners.</summary>
    public bool TryGetTargetedPair(
        Entity<WFPlanetCrackerComponent> ent,
        out Entity<WFGravityAnchorComponent> a,
        out Entity<WFGravityAnchorComponent> b)
    {
        b = default;

        if (!TryGetAnchor(ent.Comp.AnchorA, out a) || !TryGetAnchor(ent.Comp.AnchorB, out b))
            return false;

        return a.Comp.Partner == GetNetEntity(b.Owner) && b.Comp.Partner == GetNetEntity(a.Owner);
    }

    /// <summary>
    /// The pair this hull owns and could target, found by query rather than stored: the links are a handful of
    /// entities and a stored copy would need invalidating on every anchor edge.
    /// </summary>
    public bool TryGetOwnedPair(
        Entity<WFPlanetCrackerComponent> ent,
        out Entity<WFGravityAnchorComponent> a,
        out Entity<WFGravityAnchorComponent> b,
        bool lockedOnly)
    {
        a = default;
        b = default;

        var owner = GetNetEntity(ent.Owner);
        var query = EntityQueryEnumerator<WFGravityAnchorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Cracker != owner || comp.Partner is null)
                continue;

            if (!TryGetAnchor(comp.Partner, out var partner) || partner.Comp.Cracker != owner)
                continue;

            if (lockedOnly && (comp.State != WFAnchorState.Locked || partner.Comp.State != WFAnchorState.Locked))
                continue;

            a = (uid, comp);
            b = partner;
            return true;
        }

        return false;
    }

    /// <summary>Every gravity projector resting on this hull, ordered by grid-local X so the console rows are stable.</summary>
    public void GetProjectors(EntityUid cracker, List<Entity<WFGravityProjectorComponent>> into)
    {
        into.Clear();

        var query = EntityQueryEnumerator<WFGravityProjectorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid == cracker)
                into.Add((uid, comp));
        }

        into.Sort((x, y) => Transform(x.Owner).LocalPosition.X.CompareTo(Transform(y.Owner).LocalPosition.X));
    }

    /// <summary>The hull's centrifuge, if it has one; the grace timer's first precondition.</summary>
    public bool TryGetCentrifuge(EntityUid cracker, out Entity<WFCentrifugeComponent> centrifuge)
    {
        centrifuge = default;

        var query = EntityQueryEnumerator<WFCentrifugeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid != cracker)
                continue;

            centrifuge = (uid, comp);
            return true;
        }

        return false;
    }
}
