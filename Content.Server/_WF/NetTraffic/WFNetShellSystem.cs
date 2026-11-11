using System.Numerics;
using Content.Server._Mono.Projectiles.TargetGuided;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Shared._Goobstation.Wizard.Projectiles;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared._WF.NetTraffic;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Enums;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._WF.NetTraffic;

/// <summary>
/// Keeps unguided ship shells off the network. Each is put on a layer no eye sees; the players near its flight get one
/// message when it is fired, another if its course changes and one when it ends, and their clients draw it.
/// </summary>
public sealed class WFNetShellSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFViewPointSystem _viewPoints = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VisibilitySystem _visibility = default!;

    /// <summary>A course change smaller than this, in metres a second, isn't sent.</summary>
    private const float VelocityTolerance = 0.5f;

    /// <summary>The furthest a shell's flight reaches when deciding who is told about it.</summary>
    private const float MaxReach = 3000f;

    private bool _enabled;
    private float _margin;
    private int _nextId;
    private readonly Dictionary<ICommonSession, WFShellBatchEvent> _batches = new();
    private readonly List<(ICommonSession Session, MapCoordinates At)> _viewers = new();
    private readonly HashSet<ICommonSession> _told = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, NetTrafficCVars.Shells, value => _enabled = value, true);
        Subs.CVar(_config, NetTrafficCVars.ShellMargin, value => _margin = value, true);
        SubscribeLocalEvent<ShipWeaponProjectileComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFNetShellComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>Hides an unguided shell from every client the moment it exists, before it is ever sent.</summary>
    private void OnMapInit(Entity<ShipWeaponProjectileComponent> ent, ref MapInitEvent args)
    {
        if (!_enabled
            || HasComp<TargetGuidedComponent>(ent)
            || HasComp<TargetSeekingComponent>(ent)
            || HasComp<HomingProjectileComponent>(ent))
            return;

        var shell = AddComp<WFNetShellComponent>(ent);
        shell.Id = ++_nextId;
        _visibility.AddLayer(ent.Owner, WFNetTraffic.NeverSentLayer);
    }

    private void OnShutdown(Entity<WFNetShellComponent> ent, ref ComponentShutdown args)
    {
        if (!ent.Comp.Announced || ent.Comp.Viewers.Count == 0)
            return;

        var ended = new WFShellEnded(ent.Comp.Id, _transform.GetWorldPosition(ent));
        foreach (var session in ent.Comp.Viewers)
        {
            Batch(session).Ended.Add(ended);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _viewPoints.Collect(_viewers);

        var query = EntityQueryEnumerator<WFNetShellComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var shell, out var xform))
        {
            var velocity = _physics.GetMapLinearVelocity(uid);
            if (!shell.Announced)
            {
                // A shell spawns before its gun gives it a speed; it is announced once it moves.
                if (velocity.LengthSquared() > 0f)
                    Announce((uid, shell, xform), velocity);
                continue;
            }

            if ((velocity - shell.Velocity).LengthSquared() < VelocityTolerance * VelocityTolerance)
                continue;

            shell.Velocity = velocity;
            var moved = new WFShellMoved(shell.Id, _transform.GetWorldPosition(xform), velocity,
                (float) _transform.GetWorldRotation(xform).Theta, _timing.CurTick.Value);
            foreach (var session in shell.Viewers)
            {
                Batch(session).Moved.Add(moved);
            }
        }

        foreach (var (session, batch) in _batches)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(batch, session);
        }

        _batches.Clear();
    }

    private void Announce(Entity<WFNetShellComponent, TransformComponent> ent, Vector2 velocity)
    {
        var (uid, shell, xform) = ent;
        shell.Announced = true;
        shell.Velocity = velocity;
        if (xform.MapUid is not { } map || MetaData(uid).EntityPrototype is not { } proto)
            return;

        var lifetime = TryComp<TimedDespawnComponent>(uid, out var despawn) ? despawn.Lifetime : 5f;
        var position = _transform.GetWorldPosition(xform);
        var reach = MathF.Min(MaxReach, velocity.Length() * lifetime) + _margin;
        var fired = new WFShellFired(shell.Id, proto.ID, GetNetEntity(map), position, velocity,
            (float) _transform.GetWorldRotation(xform).Theta, _timing.CurTick.Value, lifetime);
        _told.Clear();
        _viewPoints.InRange(_viewers, new MapCoordinates(position, xform.MapID), reach, _told);
        foreach (var session in _told)
        {
            shell.Viewers.Add(session);
            Batch(session).Fired.Add(fired);
        }
    }

    private WFShellBatchEvent Batch(ICommonSession session)
    {
        if (!_batches.TryGetValue(session, out var batch))
            _batches[session] = batch = new WFShellBatchEvent();
        return batch;
    }
}
