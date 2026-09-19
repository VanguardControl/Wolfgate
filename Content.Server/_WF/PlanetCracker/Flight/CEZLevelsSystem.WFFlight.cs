using System.Numerics;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.PlanetCracker.Flight;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Flight;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Destructible;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._CE.ZLevels.Core;

/// <summary>
/// Atmospheric flight (F10): what holds a hull up over a planet, how fast it comes down when nothing does, and what a
/// touchdown at the bottom of that costs. CE's own lift is a gravity generator; on a planet layer it is
/// thrusters (ordinary engines at reduced efficiency), so every hook here is reached from a marked line in the CE gravity, pilot and wall
/// passes rather than duplicating them.
/// </summary>
public sealed partial class CEZLevelsSystem
{
    /// <summary>Crashed hulls stop paying hover power while grounded; an ascent command restores full demand.</summary>
    public bool WfWreckResting(EntityUid grid) =>
        HasComp<WFCrashImpactComponent>(grid) && WfHasSkidGround(grid) &&
        !HasComp<WFLiftoffComponent>(grid) && _pilotVerticalInput.GetValueOrDefault(grid) <= 0f;

    [Dependency] private SharedDestructibleSystem _wfDestructible = default!;
    [Dependency] private WFFlightSystem _wfFlight = default!;
    [Dependency] private ThrusterSystem _wfThrusters = default!;

    /// <summary>
    /// Lift ratio at or above which a hull flies exactly as CE flies one today. Below it the hull is in lift lost.
    /// </summary>
    public const float WFFullLiftRatio = 1f;

    /// <summary>
    /// Lift ratio under which partial lift stops helping at all and the hull falls at CE's full grid gravity. Between
    /// this and <see cref="WFFullLiftRatio"/> the downward acceleration is scaled by (1 - ratio), so a hull just shy
    /// of flying sinks gently and one at half lift comes down at half speed.
    /// </summary>
    public const float WFPartialLiftRatio = 0.5f;

    /// <summary>
    /// Fraction of the speed a free fall actually arrives at, at or above which a touchdown is a crash rather than a
    /// hard landing. Measured against <see cref="WfGetFreeFallSpeed(CEZGridFallerComponent)"/> and never against CE's
    /// 1.2 terminal velocity: a plummet is stopped and restarted at every layer boundary, so it only ever reaches
    /// about 0.47 levels/s and the old fixed 0.8 threshold made every free fall in the game a hard landing. A free
    /// fall lands at the reference itself; the partial-lift band lands at roughly sqrt(1 - r) of it, 0.74 at the 0.5
    /// floor, so the fraction sits above that and the whole band survives its own landing.
    /// </summary>
    public const float WFHardLandingFraction = 0.8f;

    /// <summary>Step the reference free fall is integrated at; CE's own fall runs on the server tick.</summary>
    private const float WFFreeFallStep = 1f / 60f;

    /// <summary>Longest reference fall that is integrated, in seconds, so an unreachable gap cannot hang the loop.</summary>
    private const float WFFreeFallCeiling = 600f;

    /// <summary>How often a pilot leaning on the descend key out of orbit is told where the button is.</summary>
    private static readonly TimeSpan WFOrbitRefusalCooldown = TimeSpan.FromSeconds(4);

    /// <summary>Speed (m/s) a skidding hull loses for each anchored obstacle it ploughs through.</summary>
    private const float WFPloughSpeedCost = 1.5f;

    private readonly Dictionary<EntityUid, TimeSpan> _wfNextOrbitRefusal = new();

    private readonly HashSet<EntityUid> _wfPloughed = new();

    private readonly Dictionary<(float Gravity, float Terminal), float> _wfFreeFallSpeeds = new();

    /// <summary>
    /// Surface gravity of the planet a grid is flying over, or false when it is not over one at all. A transit map
    /// carries no layer marker of its own, so the gap answers with the layer under it - which is the layer the hull
    /// is falling towards, and the one whose gravity is pulling on it.
    /// </summary>
    public bool WfTryGetPlanetGravity(EntityUid grid, out float gravity)
    {
        gravity = 1f;

        if (Transform(grid).MapUid is not { } mapUid)
            return false;

        if (TryComp<WFPlanetLayerComponent>(mapUid, out var layer))
        {
            gravity = layer.Gravity;
            return true;
        }

        if (!TryComp<CEZTransitMapComponent>(mapUid, out var transit))
            return false;

        var anchor = transit.LowerMap ?? transit.UpperMap;

        if (anchor is not { } anchorMap || !TryComp<WFPlanetLayerComponent>(anchorMap, out var anchorLayer))
            return false;

        gravity = anchorLayer.Gravity;
        return true;
    }

    /// <summary>True when this grid is flying over a planet, where the landing-thruster rule replaces the gravgen one.</summary>
    public bool WfIsPlanetFlight(EntityUid grid) => WfTryGetPlanetGravity(grid, out _);

    /// <summary>Lift one grid's own landing thrusters are producing right now, before the planet's gravity divides it.</summary>
    public float WfGetLandingThrust(EntityUid grid)
    {
        var lift = 0f;
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (TryComp<ThrusterComponent>(uid, out var thruster))
                lift += _wfThrusters.WfAtmosphericForce(uid, thruster) / ThrusterSystem.WfStandardGravity;
        }
        return lift;
    }

    /// <summary>Fraction of thrust left for manoeuvres after supporting the ship's weight.</summary>
    public float WfManeuveringFactor(EntityUid grid)
    {
        if (!_wfThrusters.WfInAtmosphere(grid) || !WfTryGetLiftRatio(grid, out var ratio))
            return 1f;
        return ratio <= 1f ? 0f : Math.Clamp(1f - 1f / ratio, 0f, 1f);
    }

    public void WfGetAtmospherePower(EntityUid grid, out float demand, out bool deficit)
    {
        _wfThrusters.WfAtmospherePower(CollectRigidSet(grid), out demand, out deficit);
    }

    /// <summary>
    /// Pooled lift over pooled weight for the whole rigid body a grid belongs to, on the planet it is over. One is
    /// level flight; the console colours the readout off it and the sink scales off it.
    /// </summary>
    /// <param name="grid">Any member of the rigid body.</param>
    /// <param name="ratio">Lift over weight, or positive infinity for a weightless or force-anchored set.</param>
    public bool WfTryGetLiftRatio(EntityUid grid, out float ratio)
    {
        ratio = 0f;

        if (!WfTryGetPlanetGravity(grid, out var gravity) || gravity <= 0f)
            return false;

        var lift = 0f;
        var mass = 0f;

        foreach (var member in CollectRigidSet(grid))
        {
            if (HasComp<CEZMappingAnchorGridComponent>(member))
            {
                ratio = float.PositiveInfinity;
                return true;
            }

            lift += WfGetLandingThrust(member);

            if (_physQuery.TryComp(member, out var body))
                mass += body.FixturesMass;

            mass += GetWFVirtualMass(member);
        }

        ratio = mass <= 0f ? float.PositiveInfinity : lift / (mass * gravity);
        return true;
    }

    /// <summary>
    /// True when a gravity generator sits on a hull that is flying over a planet, where it is no lift at all. A
    /// gravgen still carries a CE station's whole z-network; this only takes it away on a WF planet network, which is
    /// what makes the cracker's centrifuge stop counting as lift the moment the hull is over a world.
    /// </summary>
    private bool WfGravgenIsOnPlanet(EntityUid gridUid)
    {
        return _mapGridQuery.HasComp(gridUid) && WfIsPlanetFlight(gridUid);
    }

    /// <summary>
    /// Adds every hull's landing-thruster lift to the sweep's pooled capacity, already divided by the planet's
    /// gravity so CE's own "pooled mass fits inside pooled capacity" test is exactly "lift ratio at least one".
    /// </summary>
    private void WfAddLandingThrusterCapacity(Dictionary<EntityUid, float> capacity)
    {
        var query = EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var thruster, out var xform))
        {
            var grid = xform.ParentUid;
            if (!_mapGridQuery.HasComp(grid) || !WfTryGetPlanetGravity(grid, out var gravity) || gravity <= 0f)
                continue;
            capacity[grid] = capacity.GetValueOrDefault(grid)
                + _wfThrusters.WfAtmosphericForce(uid, thruster) / (ThrusterSystem.WfStandardGravity * gravity);
        }
    }

    /// <summary>
    /// The downward acceleration a hull with no pooled lift actually gets, and the one place lift lost begins. Off a
    /// planet nothing changes. Over one, partial lift scales the pull by (1 - ratio) and anything short of full lift
    /// is a lift-lost hull, whether the pilot chose the descent or a thruster just went out.
    /// </summary>
    private float WfSinkGravity(EntityUid grid, HashSet<EntityUid> set, float gravity)
    {
        if (!WfTryGetLiftRatio(grid, out var ratio))
            return gravity;

        // A set falls as one, so the whole convoy carries the state its lead grid measured.
        foreach (var member in set)
        {
            _wfFlight.EnterLiftLost(member, ratio);
        }

        if (ratio >= WFFullLiftRatio)
            return gravity;

        return ratio >= WFPartialLiftRatio ? gravity * (1f - ratio) : gravity;
    }

    /// <summary>
    /// True when a pilot's vertical input on an orbit layer must be dropped before anything reads it. The atmosphere
    /// is entered from the shuttle console's own button, which is the only place the lift warning and its confirm
    /// live, so the descend key is refused. The climb key is refused with it: nothing is above orbit, and CE's climb
    /// falls back to the gap BELOW when it finds no gap above (TryEnterTransit's goDown), so from the top layer an
    /// ascend input was a descent with no warning, no confirm and no popup - the one way a hull with lift still left
    /// orbit on the keys. Refused where the input is collected, so no consumer of it can be the next way around.
    /// </summary>
    private bool WfRefusesOrbitInput(EntityUid grid, float input)
    {
        if (input == 0f || Transform(grid).MapUid is not { } mapUid || !WfIsOrbitLayer(mapUid))
            return false;

        // Only a descent has a button to be pointed at; a climb out of orbit is nowhere to go at all.
        if (input < 0f && _timing.CurTime >= _wfNextOrbitRefusal.GetValueOrDefault(grid))
        {
            _wfNextOrbitRefusal[grid] = _timing.CurTime + WFOrbitRefusalCooldown;

            var pilots = EntityQueryEnumerator<PilotComponent>();
            while (pilots.MoveNext(out var pilotUid, out var pilot))
            {
                if (pilot.Console is { } console && !TerminatingOrDeleted(console) && Transform(console).GridUid == grid)
                    _popup.PopupEntity(Loc.GetString("wf-flight-descend-use-button"), console, pilotUid);
            }
        }

        return true;
    }

    /// <summary>
    /// True when a hull has enough landing-thruster lift to fly itself around a planet. CE gates vertical flight on
    /// the grid's GravityComponent, which knows only about gravity generators and so reads false on every hull whose
    /// lift is thrusters.
    /// </summary>
    private bool WfHasVerticalLift(EntityUid grid)
    {
        return WfTryGetLiftRatio(grid, out var ratio) && ratio >= WFFullLiftRatio;
    }

    /// <summary>A skid only damages a hull while its footprint touches planetary terrain.</summary>
    public bool WfHasSkidGround(EntityUid grid)
    {
        return _mapGridQuery.TryComp(grid, out var gridComp)
               && Transform(grid).MapUid is { } map
               && HasComp<WFPlanetLayerComponent>(map)
               && _mapGridQuery.HasComp(map)
               && HasGroundUnderFootprint((grid, gridComp), map);
    }

    /// <summary>
    /// The speed (levels/second) a hull that fell one whole gap under its own weight actually touches down at. CE
    /// zeroes the fall speed at every layer boundary, so this - not GridTerminalVelocity, which the taper only ever
    /// approaches - is the fastest a plummet ever arrives, and it is what both the hard-landing test and the fall's
    /// own rumble are scaled against. Cached: it is the same answer for every hull over a given world.
    /// </summary>
    public float WfGetFreeFallSpeed(CEZGridFallerComponent faller)
    {
        return WfGetFreeFallSpeed(faller.GridGravity, faller.GridTerminalVelocity);
    }

    /// <inheritdoc cref="WfGetFreeFallSpeed(CEZGridFallerComponent)"/>
    public float WfGetFreeFallSpeed(float gravity, float terminal)
    {
        if (gravity <= 0f || terminal <= 0f)
            return 0f;

        var key = (gravity, terminal);

        if (_wfFreeFallSpeeds.TryGetValue(key, out var cached))
            return cached;

        // The integrator the fall itself uses, over the one level a transit gap is worth.
        var velocity = 0f;
        var fallen = 0f;

        for (var elapsed = 0f; fallen < 1f && elapsed < WFFreeFallCeiling; elapsed += WFFreeFallStep)
        {
            velocity = ApproachTerminal(velocity, gravity, terminal, WFFreeFallStep);
            fallen += velocity * WFFreeFallStep;
        }

        _wfFreeFallSpeeds[key] = velocity;
        return velocity;
    }

    /// <summary>
    /// Whether a touchdown is a hard landing rather than a crash, and what a hard landing costs. A lift-lost hull that
    /// arrived under <see cref="WFHardLandingFraction"/> of the speed a free fall would have brought it in at keeps
    /// its hull: it pays in tiles, scaled by how fast it did arrive, and grinds the rest of its speed off.
    /// The chunk drop never reaches this - a dropped chunk is nobody's lift-lost hull - so F7 keeps its own crash.
    /// </summary>
    public bool WfTryHardLanding(Entity<MapGridComponent, CEZGridFallerComponent> ent, float impact)
    {
        if (!HasComp<WFLiftLostComponent>(ent.Owner))
            return false;

        var reference = WfGetFreeFallSpeed(ent.Comp2);

        if (reference <= 0f || impact >= reference * WFHardLandingFraction)
            return false;

        _wfFlight.HardLanding((ent.Owner, ent.Comp1), impact / reference);
        WfRefreshOrbitParking(ent.Owner, Transform(ent.Owner).MapUid);
        return true;
    }

    /// <summary>
    /// A crash that still had somewhere to go. CE's blast leaves the hull on the tile it detonated over; one that came
    /// in with real planar speed carries on through the wreck it just made instead of stopping dead. Lift-lost hulls
    /// only, so the F7 chunk drop's straight-down crash is untouched.
    /// </summary>
    public void WfSkidAfterCrash(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) || !_mapGridQuery.HasComp(grid) || !HasComp<WFLiftLostComponent>(grid))
            return;

        if (!_physQuery.TryComp(grid, out var body))
            return;

        var crashSpeed = body.LinearVelocity.Length();

        // A NaN is not a speed worth skidding on, and it is under no threshold: the comparison alone would let it in.
        if (!float.IsFinite(crashSpeed) || crashSpeed <= WFFlightSystem.SkidStopSpeed)
            return;

        // No thud: the crash was the noise this landing made, and one crash is one bang (CrashGrid).
        _wfFlight.BeginSkid(grid, thud: false);
    }

    /// <summary>Planetary ship crashes use structural breakup; extracted chunks retain their original drop.</summary>
    public bool WfTryStructuralCrash(Entity<MapGridComponent, CEZGridFallerComponent> ent, float impact)
    {
        if (!WfHasSkidGround(ent.Owner)
            || HasComp<Content.Shared._WF.PlanetCracker.Chunk.WFPlanetChunkComponent>(ent.Owner))
            return false;
        var reference = WfGetFreeFallSpeed(ent.Comp2);
        _wfFlight.StructuralCrash((ent.Owner, ent.Comp1), reference > 0f ? impact / reference : 1f);
        return true;
    }

    /// <summary>Sliding wrecks shed momentum over distance; stopped ships retain the normal static ground grip.</summary>
    public float WfCrashSkidFriction(EntityUid grid)
    {
        return HasComp<WFSkidComponent>(grid) ? 0.0125f : 1f;
    }

    /// <summary>Re-arms a grid's ordinary z-gravity after it has been taken off an orbit layer by hand.</summary>
    public void WfRearmZGravity(EntityUid grid)
    {
        WfRefreshOrbitParking(grid, Transform(grid).MapUid);
    }

    /// <summary>
    /// A hull skidding out a hard landing goes through what it hits. CE's wall pass would push it back out and bounce
    /// it; instead every anchored obstacle inside the footprint is broken and the hull pays speed for each one, so a
    /// long skid through a fence line ends where the fences run out rather than pinballing off the first post.
    /// </summary>
    private bool WfPloughThroughWalls(EntityUid grid, PhysicsComponent body)
    {
        if (!TryComp<WFSkidComponent>(grid, out var skid) || !WfHasSkidGround(grid))
            return false;

        // Breaking on the leading edge's own interval rather than every tick: a hull sitting on a fence line is in
        // contact with it for as long as it is sliding along it, and one destruction sound per post per tick is a
        // hull's worth of networked audio entities a second. Between intervals it passes through rather than bouncing.
        if (_timing.CurTime < skid.NextPlough)
            return true;

        skid.NextPlough = _timing.CurTime + WFFlightSystem.SkidBiteInterval;

        _wfPloughed.Clear();

        foreach (var contact in _wallContacts)
        {
            // A broad bounding rectangle includes empty corners and gaps in irregular hulls. Only destroy
            // obstacles which CE found touching hull tiles, once even when several tiles hit the same wall.
            var ent = contact.Wall;
            if (!TerminatingOrDeleted(ent) && _physQuery.TryComp(ent, out var obstacle) && obstacle.CanCollide && obstacle.Hard)
                _wfPloughed.Add(ent);
        }

        if (_wfPloughed.Count == 0)
            return true;

        foreach (var ent in _wfPloughed)
        {
            _wfFlight.GroundObstacleImpact(grid, _transform.GetWorldPosition(ent));
            _wfDestructible.BreakEntity(ent);

            if (!TerminatingOrDeleted(ent))
                QueueDel(ent);
        }

        var velocity = body.LinearVelocity;
        var speed = velocity.Length();
        var cost = MathF.Min(WFPloughSpeedCost * _wfPloughed.Count, speed * 0.03125f);

        // Non-finite is neither over nor under the cost, so it has to be named: dividing it back out would write a
        // NaN position onto the hull and onto everything parented to it.
        _physics.SetLinearVelocity(grid,
            !float.IsFinite(speed) || speed <= cost ? Vector2.Zero : velocity / speed * (speed - cost),
            body: body);

        return true;
    }
}
