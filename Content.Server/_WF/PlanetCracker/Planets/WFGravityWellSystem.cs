using System.Numerics;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.ShipPa;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Planets;

/// <summary>
/// A sector body's gravity well: a hull adrift in open space inside a world's orbit range, with nothing holding it on
/// station, is drawn in - slowly at the rim, faster near the body - and, once deep enough, captured onto the orbit
/// layer, where orbit decay (F11) takes over. "Adrift" is the decay system's own reading, so a ship that would keep
/// its orbit is a ship the well cannot move.
/// </summary>
public sealed partial class WFGravityWellSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private ShipPaSystem _pa = default!;
    [Dependency] private WFOrbitDecaySystem _decay = default!;
    [Dependency] private WFOrbitEntrySystem _orbitEntry = default!;

    /// <summary>Pull at the rim of the well and at the capture line, in m/s².</summary>
    public const float RimAcceleration = 0.15f;
    public const float DeepAcceleration = 1f;

    /// <summary>Infall speed the well stops adding to, at the rim and at the capture line, in m/s.</summary>
    public const float RimInfall = 2f;
    public const float DeepInfall = 8f;

    /// <summary>Fraction of the orbit range inside which an adrift hull is captured onto the orbit layer.</summary>
    public const float CaptureFraction = 0.35f;

    /// <summary>
    /// How long a hull must have been adrift inside a well before it is pulled at all, and before it can be captured.
    /// A breaker tripping, a brownout or an FTL arrival all read as "no thrust" for a moment; none of them is a wreck.
    /// </summary>
    public static readonly TimeSpan PullDelay = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan CaptureDelay = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextSweep;

    /// <summary>Hulls in a well this sweep, with the body pulling them and its orbit layer.</summary>
    private readonly Dictionary<EntityUid, (EntityUid Body, EntityUid Orbit, float Range)> _pulled = new();

    /// <summary>When each hull in a well was first seen adrift there; dropped the sweep it is not.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _adriftSince = new();

    /// <summary>Hulls adrift in a well this sweep, pulled yet or not.</summary>
    private readonly HashSet<EntityUid> _adrift = new();

    /// <summary>Hulls already warned, so the PA speaks once per fall rather than once per sweep.</summary>
    private readonly HashSet<EntityUid> _warned = new();

    private readonly List<EntityUid> _scratch = new();

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime >= _nextSweep)
        {
            _nextSweep = _timing.CurTime + SweepInterval;
            Sweep();
        }

        foreach (var (grid, well) in _pulled)
        {
            Pull(grid, well.Body, well.Range, frameTime);
        }
    }

    /// <summary>Rebuilds the set of adrift hulls inside a well, and captures the ones that have fallen deep enough.</summary>
    private void Sweep()
    {
        _pulled.Clear();
        _adrift.Clear();

        if (!_cfg.GetCVar(PlanetCrackerCVars.PlanetNetworks))
        {
            _warned.Clear();
            _adriftSince.Clear();
            return;
        }

        var bodies = EntityQueryEnumerator<WFSectorPlanetComponent, TransformComponent>();

        while (bodies.MoveNext(out var body, out var sector, out var bodyXform))
        {
            if (sector.OrbitMap is not { } orbitNet
                || !TryGetEntity(orbitNet, out var orbitUid)
                || !TryComp<WFOrbitLayerComponent>(orbitUid, out var orbit)
                || bodyXform.MapUid is not { } sectorMap)
            {
                continue;
            }

            var bodyPos = _transform.GetWorldPosition(bodyXform);
            var grids = EntityQueryEnumerator<ShuttleComponent, MapGridComponent, PhysicsComponent, TransformComponent>();

            while (grids.MoveNext(out var grid, out _, out _, out var physics, out var xform))
            {
                if (xform.MapUid != sectorMap
                    || physics.BodyType == BodyType.Static
                    || HasComp<FTLComponent>(grid)
                    || HasComp<ForceAnchorComponent>(grid))
                {
                    continue;
                }

                var distance = (bodyPos - _transform.GetWorldPosition(xform)).Length();

                if (distance > orbit.Range || _decay.HasStationKeeping(grid))
                    continue;

                _adrift.Add(grid);
                _adriftSince.TryAdd(grid, _timing.CurTime);

                if (_timing.CurTime < _adriftSince[grid] + PullDelay)
                    continue;

                // Two wells overlapping: the nearer body wins.
                if (_pulled.TryGetValue(grid, out var other)
                    && (_transform.GetWorldPosition(other.Body) - _transform.GetWorldPosition(xform)).Length() <= distance)
                {
                    continue;
                }

                _pulled[grid] = (body, orbitUid.Value, orbit.Range);
            }
        }

        _scratch.Clear();

        foreach (var (grid, well) in _pulled)
        {
            if (_warned.Add(grid))
            {
                _pa.Announce(grid, Loc.GetString("wf-gravity-well-warning",
                    ("ship", _pa.GetShipName(grid)),
                    ("planet", Name(well.Body))));
            }

            var distance = (_transform.GetWorldPosition(well.Body) - _transform.GetWorldPosition(grid)).Length();

            if (distance <= well.Range * CaptureFraction
                && _timing.CurTime >= _adriftSince[grid] + CaptureDelay
                && TryCapture(grid, well.Body, well.Orbit))
                _scratch.Add(grid);
        }

        foreach (var grid in _scratch)
        {
            _pulled.Remove(grid);
        }

        _warned.RemoveWhere(grid => !_pulled.ContainsKey(grid));

        _scratch.Clear();

        foreach (var grid in _adriftSince.Keys)
        {
            if (!_adrift.Contains(grid))
                _scratch.Add(grid);
        }

        foreach (var grid in _scratch)
        {
            _adriftSince.Remove(grid);
        }
    }

    /// <summary>Hands a hull to the orbit layer at the spot it occupies, through the same hop the console uses.</summary>
    private bool TryCapture(EntityUid grid, EntityUid body, EntityUid orbit)
    {
        if (!TryComp<ShuttleComponent>(grid, out var shuttle) || !_shuttle.CanFTL(grid, out _))
            return false;

        if (!_shuttle.WfFTLToLayer((grid, shuttle), orbit, _transform.GetWorldPosition(grid)))
            return false;

        _orbitEntry.MarkApproach(grid, body, true);
        return true;
    }

    /// <summary>One frame of pull: infall is topped up towards the local limit, never pushed past it.</summary>
    private void Pull(EntityUid grid, EntityUid body, float range, float frameTime)
    {
        if (TerminatingOrDeleted(grid) || TerminatingOrDeleted(body) || !TryComp<PhysicsComponent>(grid, out var physics))
            return;

        var offset = _transform.GetWorldPosition(body) - _transform.GetWorldPosition(grid);
        var distance = offset.Length();

        if (distance < 1f)
            return;

        var direction = offset / distance;
        var depth = 1f - Math.Clamp((distance - range * CaptureFraction) / (range * (1f - CaptureFraction)), 0f, 1f);
        var limit = MathHelper.Lerp(RimInfall, DeepInfall, depth);
        var infall = Vector2.Dot(physics.LinearVelocity, direction);

        if (infall >= limit)
            return;

        var delta = MathF.Min(MathHelper.Lerp(RimAcceleration, DeepAcceleration, depth) * frameTime, limit - infall);

        _physics.WakeBody(grid, body: physics);
        _physics.ApplyLinearImpulse(grid, direction * delta * physics.Mass, body: physics);
    }
}
