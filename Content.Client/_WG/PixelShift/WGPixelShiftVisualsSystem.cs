using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Client._WG.Waddle;
using Content.Shared._WG.PixelShift;
using Content.Shared.Buckle;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client._WG.PixelShift;

/// <summary>
/// Adds each mob's pixel shift and tilt to its sprite after animations have set it, so lunges, jitter and lying down
/// still play from the mob's own base. The shown values glide to the set ones each frame.
/// <see cref="WGPixelShiftRestoreSystem"/> takes them back off before the next frame.
/// </summary>
public sealed class WGPixelShiftVisualsSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _animation = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    /// <summary>How quickly the shown shift and tilt close on the set ones, per second.</summary>
    private const float GlideRate = 20f;

    /// <summary>Pixels or degrees from the set value at which the glide snaps onto it.</summary>
    private const float GlideSnap = 0.05f;

    /// <summary>The animation that turns the sprite when lying down, standing up or buckling.</summary>
    private const string RotateAnimationKey = "rotate";

    private EntityQuery<AppearanceComponent> _appearanceQuery;
    private EntityQuery<WGPixelShiftComponent> _shiftQuery;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(AnimationPlayerSystem));
        UpdatesAfter.Add(typeof(AppearanceSystem));
        // Waddle stacks on top of the shift, so it comes off first.
        UpdatesBefore.Add(typeof(WaddleSystem));
        UpdatesBefore.Add(typeof(CEClientZLevelsPostAnimSystem));

        _appearanceQuery = GetEntityQuery<AppearanceComponent>();
        _shiftQuery = GetEntityQuery<WGPixelShiftComponent>();

        SubscribeLocalEvent<WGPixelShiftComponent, ComponentShutdown>((uid, comp, _) => Remove((uid, comp)));
    }

    public override void FrameUpdate(float frameTime)
    {
        var step = 1f - MathF.Exp(-GlideRate * frameTime);
        var query = EntityQueryEnumerator<WGPixelShiftComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var shift, out var sprite))
        {
            var targetTilt = shift.Tilt != 0 && !RotationTaken(uid) ? shift.Tilt : 0f;
            shift.ShownOffset = Glide(shift.ShownOffset, (Vector2) shift.Offset, step);
            shift.ShownTilt = Glide(shift.ShownTilt, targetTilt, step);

            var offset = shift.ShownOffset / EyeManager.PixelsPerMeter;
            var tilt = Angle.FromDegrees(shift.ShownTilt);

            shift.AppliedOffset = offset;
            shift.AppliedTilt = tilt;
            if (offset == Vector2.Zero && tilt == Angle.Zero)
                continue;

            shift.WrittenOffset = sprite.Offset + offset;
            shift.WrittenRotation = sprite.Rotation + tilt;
            _sprite.SetOffset((uid, sprite), shift.WrittenOffset);
            _sprite.SetRotation((uid, sprite), shift.WrittenRotation);
        }
    }

    /// <summary>
    /// Whether lying down or buckling owns the sprite's rotation, including the animation that turns it either way.
    /// The tilt glides out while it does and back in once the mob is upright again.
    /// </summary>
    private bool RotationTaken(EntityUid uid)
    {
        if (_standing.IsDown(uid) || _buckle.IsBuckled(uid) || _animation.HasRunningAnimation(uid, RotateAnimationKey))
            return true;

        return _appearanceQuery.TryComp(uid, out var appearance)
            && _appearance.TryGetData<RotationState>(uid, RotationVisuals.RotationState, out var state, appearance)
            && state == RotationState.Horizontal;
    }

    private static Vector2 Glide(Vector2 shown, Vector2 target, float step)
    {
        var next = shown + (target - shown) * step;
        return Vector2.DistanceSquared(next, target) < GlideSnap * GlideSnap ? target : next;
    }

    private static float Glide(float shown, float target, float step)
    {
        var next = shown + (target - shown) * step;
        return MathF.Abs(next - target) < GlideSnap ? target : next;
    }

    /// <summary>Takes this frame's shift and tilt back off, unless something has set the sprite since.</summary>
    public void Remove(Entity<WGPixelShiftComponent> ent)
    {
        var shift = ent.Comp;
        if (shift.AppliedOffset == Vector2.Zero && shift.AppliedTilt == Angle.Zero)
            return;

        if (TryComp<SpriteComponent>(ent, out var sprite))
        {
            if (sprite.Offset.Equals(shift.WrittenOffset))
                _sprite.SetOffset((ent, sprite), sprite.Offset - shift.AppliedOffset);

            if (sprite.Rotation.Equals(shift.WrittenRotation))
                _sprite.SetRotation((ent, sprite), sprite.Rotation - shift.AppliedTilt);
        }

        shift.AppliedOffset = Vector2.Zero;
        shift.AppliedTilt = Angle.Zero;
    }

    /// <summary>
    /// The shift currently in a sprite's offset, for systems that record the offset as a base to return to.
    /// </summary>
    public Vector2 CurrentShift(EntityUid uid, SpriteComponent sprite)
    {
        return _shiftQuery.TryComp(uid, out var shift) && sprite.Offset.Equals(shift.WrittenOffset)
            ? shift.AppliedOffset
            : Vector2.Zero;
    }

    /// <summary>
    /// A sprite's offset without this frame's pixel shift, so status icons and bars drawn over the mob stay put.
    /// </summary>
    public Vector2 UnshiftedOffset(EntityUid uid, SpriteComponent sprite)
    {
        return _shiftQuery.TryComp(uid, out var shift) ? sprite.Offset - shift.AppliedOffset : sprite.Offset;
    }
}
