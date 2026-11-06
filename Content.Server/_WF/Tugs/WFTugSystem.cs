using System.Numerics;
using Content.Shared._WF.Tugs;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server._WF.Tugs;

/// <summary>Luggage tugs: the trolley train that follows the tug's path, and the crates strapped to the trolleys.</summary>
public sealed partial class WFTugSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>The tug lays a trail point each time it has moved this far.</summary>
    private const float TrailStep = 0.1f;

    /// <summary>Trail kept past the last trolley, so a short reverse has path to push back along.</summary>
    private const float TrailSlack = 1.5f;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFTugComponent, MapInitEvent>(OnTugMapInit);
        SubscribeLocalEvent<WFTugComponent, ComponentShutdown>(OnTugShutdown);
        SubscribeLocalEvent<WFTugComponent, ExaminedEvent>(OnTugExamined);

        SubscribeLocalEvent<WFTugTrolleyComponent, ComponentInit>(OnTrolleyInit);
        SubscribeLocalEvent<WFTugTrolleyComponent, ComponentStartup>(OnTrolleyStartup);
        SubscribeLocalEvent<WFTugTrolleyComponent, ComponentShutdown>(OnTrolleyShutdown);
        SubscribeLocalEvent<WFTugTrolleyComponent, GetVerbsEvent<AlternativeVerb>>(OnTrolleyGetVerbs);
        SubscribeLocalEvent<WFTugTrolleyComponent, BeingPulledAttemptEvent>(OnTrolleyPullAttempt);
        SubscribeLocalEvent<WFTugTrolleyComponent, ExaminedEvent>(OnTrolleyExamined);
        SubscribeLocalEvent<WFTugTrolleyComponent, WFTrolleyLoadDoAfterEvent>(OnLoadDoAfter);
        SubscribeLocalEvent<WFTugTrolleyComponent, WFTrolleyUnloadDoAfterEvent>(OnUnloadDoAfter);
    }

    private void OnTugMapInit(Entity<WFTugComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp(ent, out GridVehicleMoverComponent? mover))
            return;

        ent.Comp.BaseMaxSpeed = mover.MaxSpeed;
        ent.Comp.BaseMaxReverseSpeed = mover.MaxReverseSpeed;
        ent.Comp.BaseAcceleration = mover.Acceleration;
    }

    private void OnTugShutdown(Entity<WFTugComponent> ent, ref ComponentShutdown args)
    {
        UncoupleFrom(ent, 0);
    }

    private void OnTugExamined(Entity<WFTugComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-tug-examine-trolleys",
            ("count", ent.Comp.Trolleys.Count),
            ("max", ent.Comp.MaxTrolleys)));
    }

    /// <summary>A trolley saved mid-tow loads without its tug; it is put back to free physics.</summary>
    private void OnTrolleyStartup(Entity<WFTugTrolleyComponent> ent, ref ComponentStartup args)
    {
        if (!ent.Comp.Towed || ent.Comp.Tug != null)
            return;

        SetTowedPhysics(ent, false);
    }

    private void OnTrolleyShutdown(Entity<WFTugTrolleyComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Tug is not { } tug || !TryComp(tug, out WFTugComponent? tugComp))
            return;

        var index = tugComp.Trolleys.IndexOf(ent.Owner);
        if (index >= 0)
            UncoupleFrom((tug, tugComp), index);
    }

    private void OnTrolleyPullAttempt(Entity<WFTugTrolleyComponent> ent, ref BeingPulledAttemptEvent args)
    {
        if (ent.Comp.Tug != null)
            args.Cancel();
    }

    private void OnTrolleyExamined(Entity<WFTugTrolleyComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Tug != null)
            args.PushMarkup(Loc.GetString("wf-trolley-examine-coupled"));

        if (ent.Comp.Cargo?.ContainedEntity is { } cargo)
            args.PushMarkup(Loc.GetString("wf-trolley-examine-cargo", ("cargo", cargo)));
    }

    private void OnTrolleyGetVerbs(Entity<WFTugTrolleyComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var user = args.User;
        if (ent.Comp.Tug is { } tug)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wf-trolley-verb-uncouple"),
                Priority = 1,
                Act = () => TryUncouple(ent, user),
            });
        }
        else if (FindCouplingTug(ent) is { } candidate)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wf-trolley-verb-couple"),
                Priority = 1,
                Act = () => TryCouple(candidate, ent, user),
            });
        }

        AddCargoVerbs(ent, user, args);
    }

    /// <summary>Hitches a trolley behind the last car of a stationary tug's train.</summary>
    public bool TryCouple(Entity<WFTugComponent> tug, Entity<WFTugTrolleyComponent> trolley, EntityUid? user = null)
    {
        if (trolley.Comp.Tug != null)
            return false;

        if (tug.Comp.Trolleys.Count >= tug.Comp.MaxTrolleys)
        {
            Popup(trolley, user, "wf-trolley-couple-full");
            return false;
        }

        if (IsMoving(tug))
        {
            Popup(trolley, user, "wf-trolley-moving");
            return false;
        }

        var tugXform = Transform(tug);
        if (Transform(trolley).GridUid is not { } grid || tugXform.ParentUid != grid || !InCoupleRange(tug, trolley))
        {
            Popup(trolley, user, "wf-trolley-couple-far");
            return false;
        }

        if (TryComp(trolley, out PullableComponent? pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(trolley, pullable, user);

        tug.Comp.Trolleys.Add(trolley);
        trolley.Comp.Tug = tug;
        SetTowedPhysics(trolley, true);
        RebuildTrail(tug, grid);
        RefreshSpeed(tug);
        Popup(trolley, user, "wf-trolley-coupled");
        return true;
    }

    /// <summary>Unhitches a trolley, and every car behind it, from its tug.</summary>
    public bool TryUncouple(Entity<WFTugTrolleyComponent> trolley, EntityUid? user = null)
    {
        if (trolley.Comp.Tug is not { } tug || !TryComp(tug, out WFTugComponent? tugComp))
            return false;

        if (IsMoving(tug))
        {
            Popup(trolley, user, "wf-trolley-moving");
            return false;
        }

        var index = tugComp.Trolleys.IndexOf(trolley);
        if (index < 0)
            return false;

        UncoupleFrom((tug, tugComp), index);
        Popup(trolley, user, "wf-trolley-uncoupled");
        return true;
    }

    /// <summary>Drops every trolley from the given position in the train back.</summary>
    private void UncoupleFrom(Entity<WFTugComponent> tug, int index)
    {
        for (var i = tug.Comp.Trolleys.Count - 1; i >= index; i--)
        {
            var car = tug.Comp.Trolleys[i];
            tug.Comp.Trolleys.RemoveAt(i);

            if (!TryComp(car, out WFTugTrolleyComponent? trolley))
                continue;

            trolley.Tug = null;
            if (!TerminatingOrDeleted(car))
                SetTowedPhysics((car, trolley), false);
        }

        if (tug.Comp.Trolleys.Count == 0)
        {
            tug.Comp.Trail.Clear();
            tug.Comp.TrailGrid = null;
        }

        if (!TerminatingOrDeleted(tug))
            RefreshSpeed(tug);
    }

    /// <summary>A towed trolley is placed by the train, so it neither collides nor drifts.</summary>
    private void SetTowedPhysics(Entity<WFTugTrolleyComponent> trolley, bool towed)
    {
        trolley.Comp.Towed = towed;
        if (!TryComp(trolley, out PhysicsComponent? body))
            return;

        _physics.SetLinearVelocity(trolley, Vector2.Zero, body: body);
        _physics.SetAngularVelocity(trolley, 0f, body: body);
        _physics.SetBodyType(trolley, towed ? BodyType.Kinematic : BodyType.Dynamic, body: body);
        _physics.SetCanCollide(trolley, !towed, body: body);
    }

    /// <summary>Each trolley costs the tug some top speed and acceleration.</summary>
    private void RefreshSpeed(Entity<WFTugComponent> tug)
    {
        if (!TryComp(tug, out GridVehicleMoverComponent? mover) || tug.Comp.BaseMaxSpeed <= 0f)
            return;

        var count = tug.Comp.Trolleys.Count;
        var speed = MathF.Max(0.3f, 1f - count * tug.Comp.SpeedPenaltyPerTrolley);
        var accel = MathF.Max(0.3f, 1f - count * tug.Comp.AccelerationPenaltyPerTrolley);
        mover.MaxSpeed = tug.Comp.BaseMaxSpeed * speed;
        mover.MaxReverseSpeed = tug.Comp.BaseMaxReverseSpeed * speed;
        mover.Acceleration = tug.Comp.BaseAcceleration * accel;
        Dirty(tug, mover);
    }

    private bool IsMoving(EntityUid tug)
    {
        return TryComp(tug, out GridVehicleMoverComponent? mover) &&
               (mover.IsMoving || MathF.Abs(mover.CurrentSpeed) > 0.05f);
    }

    /// <summary>The tug whose train ends close enough to this trolley to take it, if any.</summary>
    private Entity<WFTugComponent>? FindCouplingTug(Entity<WFTugTrolleyComponent> trolley)
    {
        var query = EntityQueryEnumerator<WFTugComponent>();
        while (query.MoveNext(out var uid, out var tug))
        {
            if (tug.Trolleys.Count < tug.MaxTrolleys && InCoupleRange((uid, tug), trolley))
                return (uid, tug);
        }

        return null;
    }

    private bool InCoupleRange(Entity<WFTugComponent> tug, EntityUid trolley)
    {
        var back = tug.Comp.Trolleys.Count > 0 ? tug.Comp.Trolleys[^1] : tug.Owner;
        var a = _transform.GetMapCoordinates(back);
        var b = _transform.GetMapCoordinates(trolley);

        return a.MapId == b.MapId && (a.Position - b.Position).Length() <= tug.Comp.CoupleRange;
    }

    /// <summary>Lays the trail through the cars where they stand, so a fresh coupling settles in place.</summary>
    private void RebuildTrail(Entity<WFTugComponent> tug, EntityUid grid)
    {
        tug.Comp.Trail.Clear();
        tug.Comp.TrailGrid = grid;

        for (var i = tug.Comp.Trolleys.Count - 1; i >= 0; i--)
        {
            tug.Comp.Trail.Add(Transform(tug.Comp.Trolleys[i]).LocalPosition);
        }

        tug.Comp.Trail.Add(Transform(tug).LocalPosition);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WFTugComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var tug, out var xform))
        {
            if (tug.Trolleys.Count == 0)
                continue;

            UpdateTrain((uid, tug), xform);
        }
    }

    private void UpdateTrain(Entity<WFTugComponent> tug, TransformComponent xform)
    {
        // A tug that leaves its grid (a fall, a jump, another ship) leaves its train behind.
        if (tug.Comp.TrailGrid is not { } grid || xform.ParentUid != grid)
        {
            UncoupleFrom(tug, 0);
            return;
        }

        var trail = tug.Comp.Trail;
        var pos = xform.LocalPosition;

        // Backing up runs back along the trail.
        while (trail.Count >= 2)
        {
            var last = trail[^1];
            var prev = trail[^2];
            var seg = last - prev;
            if (Vector2.Dot(pos - prev, seg) >= seg.LengthSquared())
                break;

            trail.RemoveAt(trail.Count - 1);
        }

        if (trail.Count == 0 || (pos - trail[^1]).Length() >= TrailStep)
            trail.Add(pos);

        var fallback = -xform.LocalRotation.ToWorldVec();
        var needed = tug.Comp.HitchDistance + (tug.Comp.Trolleys.Count - 1) * tug.Comp.TrolleySpacing;

        for (var i = 0; i < tug.Comp.Trolleys.Count; i++)
        {
            var car = tug.Comp.Trolleys[i];
            if (TerminatingOrDeleted(car))
            {
                UncoupleFrom(tug, i);
                return;
            }

            var distance = tug.Comp.HitchDistance + i * tug.Comp.TrolleySpacing;
            var (point, forward) = SampleTrail(pos, trail, distance, fallback);
            PlaceCar(car, grid, point, forward.ToWorldAngle());
        }

        TrimTrail(pos, trail, needed + TrailSlack);
    }

    /// <summary>The point that lies a distance back along the path from the tug, and the way the path runs forward there.</summary>
    private static (Vector2 Point, Vector2 Forward) SampleTrail(Vector2 head, List<Vector2> trail, float distance, Vector2 fallback)
    {
        var front = head;
        var remaining = distance;
        var forward = -fallback;

        for (var i = trail.Count - 1; i >= 0; i--)
        {
            var back = trail[i];
            var seg = front - back;
            var length = seg.Length();
            if (length < 0.0001f)
                continue;

            forward = seg / length;
            if (remaining <= length)
                return (front - forward * remaining, forward);

            remaining -= length;
            front = back;
        }

        // Past the end of the trail the train runs straight on.
        return (front - forward * remaining, forward);
    }

    private static void TrimTrail(Vector2 head, List<Vector2> trail, float keep)
    {
        var front = head;
        var total = 0f;
        for (var i = trail.Count - 1; i >= 0; i--)
        {
            total += (front - trail[i]).Length();
            front = trail[i];
            if (total <= keep)
                continue;

            if (i > 0)
                trail.RemoveRange(0, i);
            return;
        }
    }

    private void PlaceCar(EntityUid car, EntityUid grid, Vector2 point, Angle rotation)
    {
        var xform = Transform(car);
        if (xform.ParentUid != grid)
        {
            _transform.SetCoordinates(car, xform, new EntityCoordinates(grid, point), rotation);
            return;
        }

        if ((xform.LocalPosition - point).LengthSquared() < 0.00001f &&
            Math.Abs(Angle.ShortestDistance(xform.LocalRotation, rotation).Degrees) < 0.5)
        {
            return;
        }

        _transform.SetLocalPositionRotation(car, point, rotation, xform);
    }

    private void Popup(EntityUid target, EntityUid? user, string message)
    {
        if (user is { } u)
            _popup.PopupEntity(Loc.GetString(message), target, u);
    }
}
