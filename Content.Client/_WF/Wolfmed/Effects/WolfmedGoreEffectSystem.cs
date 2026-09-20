using System.Numerics;
using Content.Shared._WF.Wolfmed.Gore;
using Robust.Client.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Timing;

namespace Content.Client._WF.Wolfmed.Effects;

/// <summary>
/// Draws G1's blood: the spray that travels away from a hit and the mark it leaves on a wall. The server
/// decides what and where; this only paints it.
/// </summary>
/// <remarks>
/// The art is greyscale, so every one of these is tinted with the victim's own blood reagent colour. That
/// colour, the RSI state and the facing all ride networked fields rather than a one-shot message, so a
/// client that walks into PVS an hour after a firefight sees the same wall the shooter did. The spray is
/// slid by hand each frame instead of being given physics: it is a picture, not an object, and there are
/// only ever a handful in the air.
/// </remarks>
public sealed class WolfmedGoreEffectSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedHitSplatterComponent, ComponentStartup>(OnSplatterStartup);
        SubscribeLocalEvent<WolfmedHitSplatterComponent, AfterAutoHandleStateEvent>(OnSplatterState);
        SubscribeLocalEvent<WolfmedBloodSplatComponent, ComponentStartup>(OnSplatStartup);
        SubscribeLocalEvent<WolfmedBloodSplatComponent, AfterAutoHandleStateEvent>(OnSplatState);
    }

    private void OnSplatterStartup(Entity<WolfmedHitSplatterComponent> splatter, ref ComponentStartup args)
    {
        splatter.Comp.StartedAt = _timing.CurTime;
        Apply(splatter);
    }

    private void OnSplatterState(Entity<WolfmedHitSplatterComponent> splatter, ref AfterAutoHandleStateEvent args) =>
        Apply(splatter);

    private void OnSplatStartup(Entity<WolfmedBloodSplatComponent> splat, ref ComponentStartup args) =>
        Apply(splat);

    private void OnSplatState(Entity<WolfmedBloodSplatComponent> splat, ref AfterAutoHandleStateEvent args) =>
        Apply(splat);

    /// <inheritdoc/>
    public override void FrameUpdate(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WolfmedHitSplatterComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var splatter, out var sprite))
        {
            if (splatter.Travel <= 0f)
                continue;

            var progress = (float) (now - splatter.StartedAt).TotalSeconds / splatter.Travel;
            _sprite.SetOffset((uid, sprite),
                new Angle(splatter.Angle).ToVec() * splatter.Distance * Math.Clamp(progress, 0f, 1f));
        }
    }

    private void Apply(Entity<WolfmedHitSplatterComponent> splatter)
    {
        if (!TryComp(splatter, out SpriteComponent? sprite))
            return;

        _sprite.SetColor((splatter.Owner, sprite), splatter.Comp.Color);
        _sprite.LayerSetRsiState((splatter.Owner, sprite), 0, splatter.Comp.State);
        // FIX1: the state is single-direction and the sprite has noRot, so its own rotation is a world
        // angle: the camera, the grid and the entity's rotation all leave it alone. The art points along
        // +X, which is where a rotation of zero puts it, so the angle goes on unchanged.
        _sprite.SetRotation((splatter.Owner, sprite), new Angle(splatter.Comp.Angle));
        _sprite.SetOffset((splatter.Owner, sprite), Vector2.Zero);
    }

    private void Apply(Entity<WolfmedBloodSplatComponent> splat)
    {
        if (!TryComp(splat, out SpriteComponent? sprite))
            return;

        _sprite.SetColor((splat.Owner, sprite), splat.Comp.Color);
        _sprite.LayerSetRsiState((splat.Owner, sprite), 0, splat.Comp.State);
        Face((splat.Owner, sprite), splat.Comp.Angle);
    }

    /// <summary>
    /// Points a directional state without turning the picture. The entity's own rotation cannot do this:
    /// the direction is a world direction, not one of the camera's. FIX1: the angle is exact, so it is
    /// snapped here to whichever directions this particular wall state actually has.
    /// </summary>
    private void Face(Entity<SpriteComponent> splat, float angle)
    {
        var world = new Angle(angle) + MathHelper.PiOver2;
        var eight = _sprite.TryGetLayer(splat.Owner, 0, out var layer, false) &&
                    layer.ActualState?.RsiDirections == RsiDirectionType.Dir8;

        splat.Comp.DirectionOverride = eight ? world.GetDir() : world.GetCardinalDir();
        splat.Comp.EnableDirectionOverride = true;
    }
}
