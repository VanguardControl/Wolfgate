using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Server.GameObjects;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.ShipPa;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Flight;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared._WF.ShipPa;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Server._WF.PlanetCracker.Planets;

namespace Content.Server._WF.PlanetCracker.Flight;

/// <summary>
/// The lift-lost state and everything that hangs off it: the PA callouts a descent walks through, the glide a hull
/// with forward speed keeps, and the skid a hard landing ends in. The physics itself stays in the CE passes, which
/// reach this system through the marked hooks in CEZLevelsSystem.WFFlight.cs.
/// </summary>
public sealed partial class WFFlightSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private WFGridAudienceSystem _audience = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ShipAlertSystem _alert = default!;
    [Dependency] private ShipPaSystem _pa = default!;

    /// <summary>Caution chime the moment the lift goes, before any callout.</summary>
    public const string AlertLiftLost = "WFAlertLiftLost";

    /// <summary>Out of orbit and sinking into the top air layer.</summary>
    public const string AlertDontSink = "WFAlertDontSink";

    /// <summary>Top air layer into the middle of the stack.</summary>
    public const string AlertSinkRate = "WFAlertSinkRate";

    /// <summary>Middle of the stack into the bottom air layer.</summary>
    public const string AlertTerrain = "WFAlertTerrain";

    /// <summary>The bottom gap, first half.</summary>
    public const string AlertTooLowTerrain = "WFAlertTooLowTerrain";

    /// <summary>The bottom gap, last seconds; the one callout that repeats.</summary>
    public const string AlertPullUp = "WFAlertPullUp";

    /// <summary>
    /// How much of its own planar speed a gliding hull gains per layer it falls through. Compounded on whatever the
    /// hull entered the fall with, which <see cref="GlideMinSpeed"/> guarantees is never nothing.
    /// </summary>
    public const float GlideGainPerLayer = 0.25f;

    /// <summary>
    /// Forward drift (m/s) a hull is given along its own facing the moment it loses lift. Without it a hull that lost
    /// its thrusters standing still fell straight down and stopped dead on the tile it landed on; with it every fall
    /// is a glide of some kind, and only a genuine hover comes down on the spot.
    /// </summary>
    public const float GlideMinSpeed = 2f;

    /// <summary>
    /// Fraction of the last gap left under which the pull-up callout starts, measured the way transit measures its
    /// own progress (1 at the layer above, 0 at the ground).
    /// </summary>
    public const float PullUpProgress = 0.35f;

    /// <summary>How often the pull-up callout is repeated while the hull is still in the air.</summary>
    public static readonly TimeSpan PullUpRepeat = TimeSpan.FromSeconds(3);

    /// <summary>How often the lift-lost alarm loop is re-cut so somebody who boarded mid-fall is inside its filter.</summary>
    public static readonly TimeSpan AlarmReissue = TimeSpan.FromSeconds(20);

    /// <summary>Volume (dB) of the lift-lost alarm loop; it plays under the staged callouts, not over them.</summary>
    public const float AlarmVolume = -6f;

    /// <summary>The caution alarm, looped on the hull for as long as the lift is gone.</summary>
    public static readonly SoundSpecifier LiftLostLoop =
        new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Flight/lift_lost.ogg");

    /// <summary>Played once at touchdown of a hard landing.</summary>
    public static readonly SoundSpecifier HardLandingSound =
        new SoundCollectionSpecifier("WFGroundCrashImpacts");

    /// <summary>The scrape, looped on the hull for as long as it is still moving.</summary>
    public static readonly SoundSpecifier SkidSound =
        new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Flight/ground_grind_loop.ogg");

    private readonly List<EntityUid> _liftLostScan = new();
    private readonly List<EntityUid> _skidScan = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // The only directed subscription in the whole flight family; nothing else subscribes this pair.
        SubscribeLocalEvent<WFLiftLostComponent, ComponentShutdown>(OnLiftLostShutdown);
        SubscribeLocalEvent<WFSkidComponent, ComponentShutdown>(OnSkidShutdown);
        SubscribeLocalEvent<GridSplitEvent>(OnSkidGridSplit);
    }

    private void OnSkidGridSplit(ref GridSplitEvent args)
    {
        if (!HasComp<WFSkidComponent>(args.Grid)
            && !(TryComp<WFCrashImpactComponent>(args.Grid, out var impact) && _timing.CurTime < impact.NextImpact))
            return;
        foreach (var fragment in args.NewGrids)
        {
            EnsureComp<WFSkidComponent>(fragment).Debris = true;
            if (TryComp<WFCrashImpactComponent>(args.Grid, out var parentImpact))
                EnsureComp<WFCrashImpactComponent>(fragment).NextImpact = parentImpact.NextImpact;
        }
        RestoreCrashLattice(args.Grid, args.NewGrids);
        SeparateCrashSections(args.Grid, args.NewGrids);
        _crashThrusters.WfDetachCrashThrust(args.Grid, args.NewGrids);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateLiftLost();
        UpdateSkids(frameTime);
    }

    /// <summary>
    /// The one place the alarm loop dies: recovery and landing both remove the component, and so does the grid going
    /// away. PlayGlobal parents its audio in nullspace, so a loop nobody stops outlives the hull it was warning.
    /// </summary>
    private void OnLiftLostShutdown(Entity<WFLiftLostComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Alarm = _audio.Stop(ent.Comp.Alarm);
    }

    /// <summary>
    /// Starts (or re-cuts) the caution alarm for everyone aboard. PlayGlobal freezes its recipient set at play time,
    /// so the loop is stopped and replayed on a cadence exactly as the evacuation alarm is.
    /// </summary>
    private void StartAlarmLoop(EntityUid grid, WFLiftLostComponent comp)
    {
        comp.Alarm = _audio.Stop(comp.Alarm);
        comp.NextAlarmLoop = _timing.CurTime + AlarmReissue;

        comp.Alarm = _audio.PlayGlobal(
            LiftLostLoop,
            _audience.Aboard(grid),
            true,
            AudioParams.Default.WithLoop(true).WithVolume(AlarmVolume))?.Entity;
    }

    /// <summary>
    /// Puts a hull into lift lost, or refreshes the ratio of one already in it. Idempotent: the CE sink hook calls
    /// this every tick of a fall, and the chosen descent calls it once before the hull has even moved.
    /// </summary>
    /// <param name="grid">The hull.</param>
    /// <param name="ratio">Lift over weight as the caller measured it.</param>
    public void EnterLiftLost(EntityUid grid, float ratio)
    {
        if (TerminatingOrDeleted(grid))
            return;

        if (TryComp<WFLiftLostComponent>(grid, out var existing))
        {
            existing.Ratio = ratio;
            Dirty(grid, existing);
            return;
        }

        if (ratio >= CEZLevelsSystem.WFFullLiftRatio)
            return;

        var comp = AddComp<WFLiftLostComponent>(grid);
        comp.Ratio = ratio;
        comp.Stage = WFFlightAlarmStage.LiftLost;
        comp.LastDepth = int.MaxValue;

        if (TryComp<PhysicsComponent>(grid, out var body))
            comp.Glide = SeedGlide(grid, body);

        // Whatever the ship was flying under comes back when the emergency is over, so the alarms borrow the code
        // rather than resetting the ship to green behind the crew's back.
        comp.PriorCode = CompOrNull<ShipAlertComponent>(grid)?.Code;

        Dirty(grid, comp);

        StartAlarmLoop(grid, comp);
        Callout(grid, AlertLiftLost);
    }

    /// <summary>
    /// The planar velocity a fall starts with. A hull already moving keeps exactly what it had; one that was holding
    /// station is nudged along its own facing, so it comes down like an aircraft rather than a dropped brick.
    /// </summary>
    private Vector2 SeedGlide(EntityUid grid, PhysicsComponent body)
    {
        var velocity = body.LinearVelocity;

        if (body.BodyType == BodyType.Static)
            return velocity;

        // Non-finite is not a speed: it is over every threshold and under every one, and kept as the glide heading it
        // is written back into the hull at the next layer. The nudge below replaces it with a real one.
        var lengthSquared = velocity.LengthSquared();

        if (float.IsFinite(lengthSquared) && lengthSquared >= GlideMinSpeed * GlideMinSpeed)
            return velocity;

        velocity = _transform.GetWorldRotation(grid).ToWorldVec() * GlideMinSpeed;
        _physics.SetLinearVelocity(grid, velocity, body: body);

        return velocity;
    }

    /// <summary>Ends the state and hands the ship its own situation code back.</summary>
    public void LeaveLiftLost(EntityUid grid)
    {
        if (!TryComp<WFLiftLostComponent>(grid, out var comp))
            return;

        var prior = comp.PriorCode;
        RemComp<WFLiftLostComponent>(grid);

        if (prior is { } code)
            _alert.SetCode(grid, code, announce: false);
    }

    /// <summary>
    /// Starts the ground-out of a hard landing: the hull keeps the speed it came in with and grinds it off, rather
    /// than being blown apart where it touched down.
    /// </summary>
    /// <param name="grid">The hull.</param>
    /// <param name="thud">False after a crash, where the blast has already been the noise the landing made.</param>
    public void BeginSkid(EntityUid grid, bool thud = true)
    {
        LeaveLiftLost(grid);

        if (HasComp<WFSkidComponent>(grid))
            return;

        var skid = AddComp<WFSkidComponent>(grid);

        if (thud && TryComp<MapGridComponent>(grid, out var hull))
        {
            var centre = _transform.ToMapCoordinates(new EntityCoordinates(grid, hull.LocalAABB.Center));
            var radius = hull.LocalAABB.Size.Length() * 0.5f + 32f;
            _audio.PlayGlobal(HardLandingSound, _audience.Aboard(grid).AddInRange(centre, radius),
                true, AudioParams.Default.WithVolume(8f));
        }

        // The moving-ground sweep starts one positional grind loop per section.
    }

    /// <summary>Walks every falling hull's callouts and glide forward, and drops the ones that are done.</summary>
    private void UpdateLiftLost()
    {
        _liftLostScan.Clear();

        var query = EntityQueryEnumerator<WFLiftLostComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            _liftLostScan.Add(uid);
        }

        foreach (var grid in _liftLostScan)
        {
            if (TerminatingOrDeleted(grid) || !TryComp<WFLiftLostComponent>(grid, out var comp))
                continue;

            if (_zLevels.WfTryGetLiftRatio(grid, out var ratio))
            {
                comp.Ratio = ratio;
                Dirty(grid, comp);

                if (ratio >= CEZLevelsSystem.WFFullLiftRatio)
                {
                    LeaveLiftLost(grid);
                    continue;
                }
            }

            if (_timing.CurTime >= comp.NextAlarmLoop)
                StartAlarmLoop(grid, comp);

            var mapUid = Transform(grid).MapUid;

            // Off a gap and back on a layer is a hull that has arrived somewhere, one way or another.
            if (!TryComp<CEZTransitMapComponent>(mapUid, out var transit))
            {
                LeaveLiftLost(grid);
                continue;
            }

            var depth = GapDepth(transit);

            if (depth < comp.LastDepth)
            {
                if (comp.LastDepth != int.MaxValue)
                    Glide(grid, comp);

                comp.LastDepth = depth;
                Advance(grid, comp, transit);
            }

            UpdateFinalApproach(grid, comp, transit);
        }
    }

    /// <summary>Depth of the layer a transit gap hangs above; the gap the hull is in is identified by it.</summary>
    private int GapDepth(CEZTransitMapComponent transit)
    {
        if (transit.LowerMap is { } lower && TryComp<CEZMapComponent>(lower, out var zMap))
            return zMap.Depth;

        return int.MaxValue;
    }

    /// <summary>
    /// The callout for the gap the hull has just fallen into. The stack's own shape decides the first and last one -
    /// out of orbit and onto the ground - and everything between simply steps the sequence along one, so a shallower
    /// or deeper world still reads through the same escalation instead of skipping or repeating a call.
    /// </summary>
    private void Advance(EntityUid grid, WFLiftLostComponent comp, CEZTransitMapComponent transit)
    {
        WFFlightAlarmStage stage;

        if (transit.UpperMap is { } upper && HasComp<WFOrbitLayerComponent>(upper))
        {
            stage = WFFlightAlarmStage.DontSink;
        }
        else if (transit.LowerMap is { } lower && IsGroundLayer(lower))
        {
            stage = WFFlightAlarmStage.TooLowTerrain;
        }
        else
        {
            stage = (WFFlightAlarmStage) Math.Clamp((int) comp.Stage + 1,
                (int) WFFlightAlarmStage.SinkRate,
                (int) WFFlightAlarmStage.Terrain);
        }

        SetStage(grid, comp, stage);
    }

    /// <summary>The last seconds of the last gap, where the pull-up call takes over and keeps repeating.</summary>
    private void UpdateFinalApproach(EntityUid grid, WFLiftLostComponent comp, CEZTransitMapComponent transit)
    {
        if (transit.LowerMap is not { } lower || !IsGroundLayer(lower))
            return;

        if (!TryComp<CEZPhysicsComponent>(grid, out var zPhys) || zPhys.LocalPosition > PullUpProgress)
            return;

        if (comp.Stage < WFFlightAlarmStage.PullUp)
        {
            SetStage(grid, comp, WFFlightAlarmStage.PullUp);
            comp.NextPullUp = _timing.CurTime + PullUpRepeat;
            return;
        }

        if (_timing.CurTime < comp.NextPullUp)
            return;

        comp.NextPullUp = _timing.CurTime + PullUpRepeat;
        Callout(grid, AlertPullUp);
    }

    /// <summary>Advances the callout sequence, never backwards, and reads the new one out.</summary>
    private void SetStage(EntityUid grid, WFLiftLostComponent comp, WFFlightAlarmStage stage)
    {
        if (stage <= comp.Stage)
            return;

        comp.Stage = stage;
        Dirty(grid, comp);

        Callout(grid, CodeFor(stage));
    }

    /// <summary>The situation code each callout sets.</summary>
    private static string CodeFor(WFFlightAlarmStage stage)
    {
        return stage switch
        {
            WFFlightAlarmStage.DontSink => AlertDontSink,
            WFFlightAlarmStage.SinkRate => AlertSinkRate,
            WFFlightAlarmStage.Terrain => AlertTerrain,
            WFFlightAlarmStage.TooLowTerrain => AlertTooLowTerrain,
            WFFlightAlarmStage.PullUp => AlertPullUp,
            _ => AlertLiftLost,
        };
    }

    /// <summary>
    /// Sets the code, or - when the ship is already on it - reads the same line out again, which is what lets the
    /// pull-up call repeat without the code machinery treating a repeat as a no-op.
    /// </summary>
    private void Callout(EntityUid grid, ProtoId<ShipAlertCodePrototype> code)
    {
        if (!_proto.TryIndex(code, out var proto))
            return;

        // Keep the PA's code, bubbles and normal speaker delivery, but do not let a failed PA
        // suppress GPWS precisely when a power failure has taken the ship's lift away.
        _alert.SetCode(grid, code, announce: false);
        var broadcast = _pa.Announce(grid,
            Loc.GetString(proto.Announcement, ("ship", _pa.GetShipName(grid))),
            proto.Sound,
            color: proto.Color);

        // The caution alarm already has its own hull-wide loop. Voice fallback is one stream,
        // only for this hull's audience, and never duplicates a successful speaker broadcast.
        if (!broadcast && code.Id != AlertLiftLost && proto.Sound != null)
            _audio.PlayGlobal(proto.Sound, _audience.Aboard(grid), true);

    }

    /// <summary>One layer's worth of glide: the hull keeps its heading and picks up speed along it.</summary>
    private void Glide(EntityUid grid, WFLiftLostComponent comp)
    {
        if (!TryComp<PhysicsComponent>(grid, out var body))
            return;

        var velocity = body.LinearVelocity;

        // A transit hop restores momentum, but the remembered heading is what a hull that lost it falls back on. A
        // non-finite one is treated as no momentum at all rather than multiplied up and handed back to the hull.
        if (!IsGlide(velocity))
            velocity = comp.Glide;

        if (!IsGlide(velocity))
            return;

        velocity *= 1f + GlideGainPerLayer;

        _physics.SetLinearVelocity(grid, velocity, body: body);

        comp.Glide = velocity;
    }

    /// <summary>Whether a velocity is real forward motion: finite, and more than the rounding either side of nothing.</summary>
    private static bool IsGlide(Vector2 velocity)
    {
        var lengthSquared = velocity.LengthSquared();

        return float.IsFinite(lengthSquared) && lengthSquared >= 0.0001f;
    }

    /// <summary>The bottom of a planet stack, which is where a fall ends.</summary>
    private bool IsGroundLayer(EntityUid map)
    {
        return HasComp<CEZGroundLayerComponent>(map)
               || (TryComp<CEZMapComponent>(map, out var zMap) && zMap.Depth == 0);
    }
}
