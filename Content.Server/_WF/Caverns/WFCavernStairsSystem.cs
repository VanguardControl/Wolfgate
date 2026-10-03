using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Caverns;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Timing;

namespace Content.Server._WF.Caverns;

/// <summary>
/// Opens the ground over stairs built in a cavern, so they are walked up to the surface and back, refuses to start
/// them where the ground can't open, and brings whatever a walker is pulling along.
/// </summary>
// The walking is CE's: the stairs are high ground under a hole. The hole itself belongs to the hole queue.
public sealed partial class WFCavernStairsSystem : SharedWFCavernStairsSystem
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCavernMouthSystem _mouths = default!;

    /// <summary>How often stairs that couldn't open the ground above try again, such as once a ship has left.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    /// <summary>How far, in tiles, something pulled may trail its puller and still follow it over the stairs.</summary>
    public const float CarryReach = 2.5f;

    /// <summary>How much rope a pull taken up again at the stairs has, in tiles: about what one started beside its puller has.</summary>
    public const float CarryRope = 1.15f;

    private TimeSpan _nextRetry;

    /// <summary>Pulls broken by their puller changing level over stairs since the last update.</summary>
    private readonly List<(EntityUid Puller, EntityUid Pulled)> _carried = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCavernStairsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFCavernStairsComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<WFCavernStairsComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PullerComponent, PullStoppedMessage>(OnPullStopped);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        CarryPulled();

        if (_timing.CurTime < _nextRetry)
            return;

        _nextRetry = _timing.CurTime + RetryInterval;

        var query = EntityQueryEnumerator<WFCavernStairsComponent>();
        while (query.MoveNext(out var uid, out var stairs))
        {
            if (!stairs.Open)
                TryOpen((uid, stairs));
        }
    }

    /// <inheritdoc/>
    public override bool CanBuild(EntityUid user, EntityCoordinates location, Direction direction)
    {
        if (!base.CanBuild(user, location, direction)
            || !TryComp<WFCavernLayerComponent>(_transform.GetMap(location), out var layer)
            || !TryComp<WFCavernGroundComponent>(layer.Ground, out var ground)
            || !TryComp<MapGridComponent>(layer.Ground, out var grid))
            return false;

        var index = _map.WorldToTile(layer.Ground, grid, _transform.ToMapCoordinates(location).Position);
        var key = _mouths.CheckStairs((layer.Ground, ground), index, ExitOf(index, direction)) switch
        {
            WFCavernStairsRefusal.None => null,
            WFCavernStairsRefusal.Hull => "wf-cavern-stairs-refused-hull",
            WFCavernStairsRefusal.Exit => "wf-cavern-stairs-refused-exit",
            _ => "wf-cavern-stairs-refused-built",
        };

        if (key == null)
            return true;

        _popup.PopupEntity(Loc.GetString(key), user, user);
        return false;
    }

    /// <summary>Opens the ground over stairs standing in a cavern; false while something above refuses.</summary>
    public bool TryOpen(Entity<WFCavernStairsComponent> ent)
    {
        var xform = Transform(ent);

        if (!xform.Anchored || !TryGetSite(xform, out var ground, out var index))
            return false;

        var exit = ExitOf(index, xform.LocalRotation.GetCardinalDir());
        if (_mouths.TryOpenStairs(ground, index, exit) != WFCavernStairsRefusal.None)
            return false;

        ent.Comp.Open = true;
        _adminLog.Add(LogType.Tile, LogImpact.Medium,
            $"{ToPrettyString(ent):entity} opened the ground above them at {index} on {ToPrettyString(ground)}");
        return true;
    }

    private void OnMapInit(Entity<WFCavernStairsComponent> ent, ref MapInitEvent args)
    {
        TryOpen(ent);
    }

    /// <summary>Leaves the hole to the hole queue, which gives it the landing and climb point the stairs stood in for.</summary>
    private void OnTerminating(Entity<WFCavernStairsComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryGetSite(Transform(ent), out var ground, out var index))
            _mouths.RefitHole(ground, index);
    }

    private void OnExamined(Entity<WFCavernStairsComponent> ent, ref ExaminedEvent args)
    {
        if (!TryGetSite(Transform(ent), out var ground, out var index))
            return;

        args.PushMarkup(Loc.GetString(_mouths.IsOpenAbove(ground, index)
            ? "wf-cavern-stairs-examine-open"
            : "wf-cavern-stairs-examine-blocked"));
    }

    /// <summary>Notes a pull that broke because its puller changed level over stairs: a change of map clears every joint.</summary>
    // The pulled entity follows on the next update, not in here: this runs inside the puller's own map change.
    private void OnPullStopped(EntityUid uid, PullerComponent component, PullStoppedMessage args)
    {
        if (args.PullerUid != uid || TerminatingOrDeleted(args.PulledUid))
            return;

        var puller = Transform(uid);

        if (puller.MapUid != Transform(args.PulledUid).MapUid && OverStairs(puller))
            _carried.Add((uid, args.PulledUid));
    }

    /// <summary>Brings what was pulled over the stairs to its puller's level, where the puller changed it, and takes hold of it again.</summary>
    private void CarryPulled()
    {
        if (_carried.Count == 0)
            return;

        foreach (var (puller, pulled) in _carried)
        {
            if (TerminatingOrDeleted(puller) || TerminatingOrDeleted(pulled))
                continue;

            var pullerXform = Transform(puller);

            if (pullerXform.MapUid is not { } to || Transform(pulled).MapUid is not { } from || to == from)
                continue;

            int offset;
            if (TryComp<WFCavernLayerComponent>(from, out var below) && below.Ground == to)
                offset = 1;
            else if (TryComp<WFCavernLayerComponent>(to, out var layer) && layer.Ground == from)
                offset = -1;
            else
                continue;

            var position = _transform.GetWorldPosition(pullerXform);

            if (Vector2.Distance(position, _transform.GetWorldPosition(pulled)) > CarryReach
                || !TryComp<CEZPhysicsComponent>(puller, out var pullerLevel)
                || !HasComp<CEZPhysicsComponent>(pulled)
                || !_zLevels.TryMove(pulled, offset)
                || Transform(pulled).MapUid != to)
                continue;

            // Onto the puller's own spot, which the stairs hold up on this level: behind it they don't.
            _transform.SetWorldPosition(pulled, position);
            _zLevels.SetZPosition(pulled, pullerLevel.LocalPosition);
            _zLevels.SetZVelocity(pulled, 0f);

            if (!_pulling.TryStartPull(puller, pulled)
                || !TryComp<PullableComponent>(pulled, out var pullable)
                || !TryComp<JointComponent>(pulled, out var joints))
                continue;

            // A pull's rope is as long as its two ends were apart, which here is nothing.
            var id = pullable.PullJointId;
            if (id != null && joints.GetJoints.TryGetValue(id, out var joint) && joint is DistanceJoint rope)
                rope.MaxLength = CarryRope;
        }

        _carried.Clear();
    }

    /// <summary>Whether an entity is on stairs in a cavern, or over them on the ground above.</summary>
    private bool OverStairs(TransformComponent xform)
    {
        var cavern = xform.MapUid;

        if (TryComp<WFCavernGroundComponent>(cavern, out var ground))
            cavern = ground.Cavern;

        if (cavern is not { } level || !HasComp<WFCavernLayerComponent>(level) || !TryComp<MapGridComponent>(level, out var grid))
            return false;

        var anchored = _map.GetAnchoredEntitiesEnumerator(level, grid, _map.WorldToTile(level, grid, _transform.GetWorldPosition(xform)));

        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<WFCavernStairsComponent>(uid))
                return true;
        }

        return false;
    }

    /// <summary>The ground over stairs standing in a cavern, and the ground tile above them.</summary>
    private bool TryGetSite(TransformComponent xform, out Entity<WFCavernGroundComponent> ground, out Vector2i index)
    {
        ground = default;
        index = default;

        if (!TryComp<WFCavernLayerComponent>(xform.MapUid, out var layer)
            || !TryComp<WFCavernGroundComponent>(layer.Ground, out var comp)
            || !TryComp<MapGridComponent>(layer.Ground, out var grid))
            return false;

        ground = (layer.Ground, comp);
        index = _map.WorldToTile(layer.Ground, grid, _transform.GetWorldPosition(xform));
        return true;
    }
}
