using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server._WF.ShipPa;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.LockOn;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle.Components;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.LockOn;

/// <summary>
/// One firing solution per ship. A helm or gunnery console asks for a lock under a radar click; the ship's
/// <see cref="WFLockOnComponent"/> carries it to every console aboard, points missiles at it, and warns the
/// ship that has been locked.
/// </summary>
public sealed partial class WFLockOnSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ShipPaSystem _pa = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    /// <summary>How often a held lock is revalidated for a deleted, moved or distant target.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>How often the locked point's position and velocity are refreshed for the lead indicator.</summary>
    public static readonly TimeSpan AimInterval = TimeSpan.FromSeconds(0.05);

    /// <summary>Half-width of the box used to find what sits under a click, matching one tile.</summary>
    private const float ClickRadius = 15f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShuttleConsoleComponent, WFLockOnRequestMessage>(OnHelmRequest);
        SubscribeLocalEvent<FireControlConsoleComponent, WFLockOnRequestMessage>(OnGunneryRequest);
    }

    private void OnHelmRequest(EntityUid uid, ShuttleConsoleComponent console, WFLockOnRequestMessage args)
    {
        if (CanOperate(uid, ShuttleConsoleUiKey.Key, args.Actor))
            RequestLock(uid, args.Actor, args.Coordinates);
    }

    private void OnGunneryRequest(EntityUid uid, FireControlConsoleComponent console, WFLockOnRequestMessage args)
    {
        if (CanOperate(uid, FireControlConsoleUiKey.Key, args.Actor))
            RequestLock(uid, args.Actor, args.Coordinates);
    }

    /// <summary>Whether the actor is at the console, or flying it from the cockpit, and it can hold a lock.</summary>
    private bool CanOperate(EntityUid uid, Enum key, EntityUid actor)
    {
        if (TerminatingOrDeleted(uid) || !_power.IsPowered(uid) || !Transform(uid).Anchored)
            return false;

        if (!_ui.IsUiOpen(uid, key, actor))
            return false;

        if (CanReach(uid, key, actor))
            return true;

        // A cockpit pilot is buckled into a seat on the same hull, away from the console itself.
        return TryComp<BuckleComponent>(actor, out var buckle) && buckle.Buckled &&
               Transform(actor).GridUid is { } grid &&
               Transform(uid).GridUid == grid;
    }

    /// <summary>The gunnery UI skips engine input validation, so requests recheck interaction and range as the engine would.</summary>
    private bool CanReach(EntityUid uid, Enum key, EntityUid actor) =>
        _blocker.CanInteract(actor, uid) &&
        (HasComp<IgnoreUIRangeComponent>(actor) ||
         _interaction.InRangeAndAccessible(actor, uid, _ui.GetUiRange(uid, key)));

    /// <summary>Locks the target under a click, or clears the lock when the click found nothing.</summary>
    public bool RequestLock(EntityUid console, EntityUid actor, NetCoordinates coordinates)
    {
        if (TerminatingOrDeleted(console) || Transform(console).GridUid is not { } ship)
            return false;

        if (!TryGetMapCoordinates(coordinates, console, out var point))
            return false;

        var lockOn = EnsureComp<WFLockOnComponent>(ship);
        var target = ResolveTarget(console, ship, point);
        if (target is { } found && !InRange(ship, found, lockOn))
        {
            if (CanPopup(actor))
                _popup.PopupEntity(Loc.GetString("wf-lockon-out-of-range"), console, actor);
            return false;
        }

        SetLock(ship, lockOn, console, actor, target, point);
        return true;
    }

    private bool TryGetMapCoordinates(NetCoordinates net, EntityUid console, out MapCoordinates point)
    {
        point = default;
        var coordinates = GetCoordinates(net);
        if (!coordinates.IsValid(EntityManager))
            return false;

        point = _transform.ToMapCoordinates(coordinates);
        return point.MapId != MapId.Nullspace &&
               point.MapId == Transform(console).MapID &&
               float.IsFinite(point.Position.X) &&
               float.IsFinite(point.Position.Y);
    }

    private void SetLock(EntityUid ship, WFLockOnComponent lockOn, EntityUid console, EntityUid actor,
        EntityUid? target, MapCoordinates point)
    {
        if (target == lockOn.Target)
        {
            ClearLock(ship, lockOn);
            return;
        }

        lockOn.Target = target;
        lockOn.LockedBy = console;
        lockOn.Operator = actor.Valid ? actor : null;
        lockOn.LockedAt = _timing.CurTime;

        if (target is { } lockedTarget)
            SetAim(ship, lockOn, lockedTarget, point);
        else
            ClearAim(lockOn);

        Dirty(ship, lockOn);

        if (CanPopup(actor))
        {
            var message = target is { } locked && TryComp<MetaDataComponent>(locked, out var meta)
                ? Loc.GetString("wf-lockon-acquired", ("target", meta.EntityName))
                : Loc.GetString("wf-lockon-released");
            _popup.PopupEntity(message, console, actor);
        }

        if (target is { } acquired && TargetGrid(acquired) is { } targetGrid && targetGrid != ship)
            Alert(targetGrid, ship);
    }

    /// <summary>Clears the ship's lock, if any.</summary>
    public void ClearLock(EntityUid ship, WFLockOnComponent? lockOn = null)
    {
        if (!Resolve(ship, ref lockOn) || lockOn.Target == null)
            return;

        var operatorUid = lockOn.Operator;
        var console = lockOn.LockedBy ?? ship;

        lockOn.Target = null;
        lockOn.LockedBy = null;
        lockOn.Operator = null;
        ClearAim(lockOn);
        Dirty(ship, lockOn);

        if (CanPopup(operatorUid))
            _popup.PopupEntity(Loc.GetString("wf-lockon-released"), console, operatorUid!.Value);
    }

    /// <summary>Records the clicked hull point in the target's own frame and seeds the aim state every console reads.</summary>
    private void SetAim(EntityUid ship, WFLockOnComponent lockOn, EntityUid target, MapCoordinates click)
    {
        lockOn.TargetLocalPoint = Vector2.Zero;
        if (TryComp<TransformComponent>(target, out var xform))
        {
            var (_, _, matrix) = _transform.GetWorldPositionRotationMatrix(xform);
            if (Matrix3x2.Invert(matrix, out var inverse))
                lockOn.TargetLocalPoint = Vector2.Transform(click.Position, inverse);
        }

        lockOn.NextAim = _timing.CurTime + AimInterval;
        RefreshAim(ship, lockOn, target);
    }

    /// <summary>Re-reads the aim point's world position and velocity, including the target's spin about its centre.</summary>
    private void RefreshAim(EntityUid ship, WFLockOnComponent lockOn, EntityUid target)
    {
        if (!TryComp<TransformComponent>(target, out var xform))
            return;

        var (origin, _, matrix) = _transform.GetWorldPositionRotationMatrix(xform);
        var point = Vector2.Transform(lockOn.TargetLocalPoint, matrix);
        var body = CompOrNull<PhysicsComponent>(target);
        var velocity = _physics.GetMapLinearVelocity(target, body, xform);
        var angular = _physics.GetMapAngularVelocity(target, body, xform);
        if (MathF.Abs(angular) > 1e-4f)
            velocity += Vector2Helpers.Cross(angular, point - origin);

        lockOn.AimPoint = point;
        lockOn.AimVelocity = velocity;
        lockOn.AimTime = (float) _timing.CurTime.TotalSeconds;
        Dirty(ship, lockOn);
    }

    /// <summary>Forgets the aim point when the lock is released.</summary>
    private static void ClearAim(WFLockOnComponent lockOn)
    {
        lockOn.AimPoint = default;
        lockOn.AimVelocity = default;
        lockOn.AimTime = 0f;
        lockOn.TargetLocalPoint = default;
        lockOn.NextAim = TimeSpan.Zero;
    }

    /// <summary>The grid a locked entity belongs to, so locking a wall locks its ship.</summary>
    public EntityUid? TargetGrid(EntityUid? target)
    {
        if (target is not { } uid || TerminatingOrDeleted(uid) || !EntityManager.EntityExists(uid))
            return null;
        if (!TryComp<TransformComponent>(uid, out var xform))
            return null;

        return xform.GridUid ?? (HasComp<MapGridComponent>(uid) ? uid : null);
    }

    /// <summary>The ship's current lock target, if it has one.</summary>
    public EntityUid? GetTarget(EntityUid ship)
    {
        return TryComp<WFLockOnComponent>(ship, out var lockOn) ? lockOn.Target : null;
    }

    private bool CanPopup(EntityUid? uid) =>
        uid is { Valid: true } value && HasComp<ActorComponent>(value);

    private bool InRange(EntityUid ship, EntityUid target, WFLockOnComponent lockOn)
    {
        if (!TryComp<TransformComponent>(target, out var targetXform) || targetXform.MapID != Transform(ship).MapID)
            return false;

        return Vector2.Distance(ShipCenter(ship), _transform.GetWorldPosition(targetXform)) <= lockOn.MaxRange;
    }

    private Vector2 ShipCenter(EntityUid ship)
    {
        return TryComp<MapGridComponent>(ship, out var grid)
            ? _transform.ToMapCoordinates(new EntityCoordinates(ship, grid.LocalAABB.Center)).Position
            : _transform.GetWorldPosition(Transform(ship));
    }

    public override void Update(float frameTime)
    {
        var expired = new List<EntityUid>();
        var shots = EntityQueryEnumerator<WFLockOnShotComponent>();
        while (shots.MoveNext(out var weapon, out var shot))
        {
            if (shot.Expires <= _timing.CurTime)
                expired.Add(weapon);
        }

        foreach (var weapon in expired)
            RemComp<WFLockOnShotComponent>(weapon);

        var locks = EntityQueryEnumerator<WFLockOnComponent>();
        while (locks.MoveNext(out var ship, out var lockOn))
        {
            var target = lockOn.Target;
            TransformComponent? targetXform = null;
            var valid = target is { } locked &&
                        !TerminatingOrDeleted(locked) &&
                        EntityManager.EntityExists(locked) &&
                        TryComp<TransformComponent>(locked, out targetXform) &&
                        targetXform.MapID == Transform(ship).MapID;

            // The lead indicator needs the point and its velocity faster than the integrity check does.
            if (valid && lockOn.NextAim <= _timing.CurTime)
            {
                lockOn.NextAim = _timing.CurTime + AimInterval;
                RefreshAim(ship, lockOn, target!.Value);
            }

            if (lockOn.NextCheck > _timing.CurTime)
                continue;
            lockOn.NextCheck = _timing.CurTime + CheckInterval;

            if (!valid ||
                Vector2.Distance(ShipCenter(ship), _transform.GetWorldPosition(targetXform!)) > lockOn.MaxRange * 1.25f)
            {
                lockOn.Target = null;
                lockOn.LockedBy = null;
                lockOn.Operator = null;
                ClearAim(lockOn);
                Dirty(ship, lockOn);
            }
        }
    }
}
