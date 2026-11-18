using System.Numerics;
using Content.Shared._WG.FarPrediction;
using Robust.Shared.Configuration;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Physics.Systems;

namespace Content.Client._WG.FarPrediction;

/// <summary>
/// Steadies predicted movement on grids far from the map origin. The client's engine solves in float world
/// coordinates, which are millimetres apart out there, so every predicted step lands a little off and the view,
/// which follows the player, shakes. After each step this puts a predicted body where the same step lands when taken
/// in the grid's own coordinates, as long as the engine's answer is within rounding of it.
/// </summary>
public sealed class WGFarPredictionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<JointComponent> _jointQuery;

    private readonly List<Pending> _pending = new();
    private bool _enabled;

    /// <summary>How many predicted steps have been corrected since the client started.</summary>
    [ViewVariables]
    public int Corrected { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        _jointQuery = GetEntityQuery<JointComponent>();

        // Positions are noted once every controller has had its turn before the solve, and corrected before any of
        // them looks at the result, so a controller that moves a body itself is never mistaken for rounding.
        SubscribeLocalEvent<PhysicsUpdateBeforeSolveEvent>(OnBeforeSolve, after: new[] { typeof(VirtualController) });
        SubscribeLocalEvent<PhysicsUpdateAfterSolveEvent>(OnAfterSolve, before: new[] { typeof(VirtualController) });

        Subs.CVar(_cfg, WGFarPredictionCVars.Enabled, value => _enabled = value, true);
    }

    private void OnBeforeSolve(ref PhysicsUpdateBeforeSolveEvent args)
    {
        _pending.Clear();

        if (!_enabled || !args.Prediction)
            return;

        foreach (var (uid, body, xform) in _physics.AwakeBodies)
        {
            if (!body.Predict || body.BodyType == BodyType.Static)
                continue;

            // A body parented straight to the map has no nearer frame to work in.
            var parent = xform.ParentUid;
            if (!parent.IsValid() || parent == xform.MapUid)
                continue;

            var (parentPosition, parentRotation) = _transform.GetWorldPositionRotation(parent);
            var world = parentPosition + parentRotation.RotateVec(xform.LocalPosition);

            if (!WGFarPredictionMath.IsFar(world))
                continue;

            _pending.Add(new Pending(uid, body, xform, parent, xform.LocalPosition, xform.LocalRotation, parentRotation, world));
        }
    }

    private void OnAfterSolve(ref PhysicsUpdateAfterSolveEvent args)
    {
        foreach (var pending in _pending)
        {
            var uid = pending.Uid;
            var body = pending.Body;
            var xform = pending.Xform;

            if (TerminatingOrDeleted(uid) || xform.ParentUid != pending.Parent)
                continue;

            // Walls and joints move a body for real; those steps are left exactly as the engine solved them.
            if (IsConstrained(uid, body))
                continue;

            var step = WGFarPredictionMath.Step(
                body.LinearVelocity,
                args.DeltaTime,
                pending.ParentRotation,
                body.LocalCenter,
                pending.LocalRotation,
                xform.LocalRotation);

            if (!WGFarPredictionMath.TryCorrect(pending.LocalPosition, step, xform.LocalPosition, pending.World, out var corrected))
                continue;

            _transform.SetLocalPositionRotation(uid, corrected, xform.LocalRotation, xform);
            Corrected++;
        }

        _pending.Clear();
    }

    /// <summary>Whether something solid is touching the body or a joint holds it.</summary>
    private bool IsConstrained(EntityUid uid, PhysicsComponent body)
    {
        if (_jointQuery.TryComp(uid, out var joints) && joints.JointCount > 0)
            return true;

        if (body.ContactCount == 0)
            return false;

        var contacts = _physics.GetContacts(uid);

        while (contacts.MoveNext(out var contact))
        {
            if (contact.IsTouching && contact.Hard && contact.Enabled)
                return true;
        }

        return false;
    }

    /// <summary>A predicted body as it stood before the solve.</summary>
    private readonly record struct Pending(
        EntityUid Uid,
        PhysicsComponent Body,
        TransformComponent Xform,
        EntityUid Parent,
        Vector2 LocalPosition,
        Angle LocalRotation,
        Angle ParentRotation,
        Vector2 World);
}
