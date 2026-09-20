using System.Numerics;
using Content.Shared._WF.Wolfmed.Gore;
using Robust.Client.GameObjects;
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
                splatter.Direction.ToVec() * splatter.Distance * Math.Clamp(progress, 0f, 1f));
        }
    }

    private void Apply(Entity<WolfmedHitSplatterComponent> splatter)
    {
        if (!TryComp(splatter, out SpriteComponent? sprite))
            return;

        _sprite.SetColor((splatter.Owner, sprite), splatter.Comp.Color);
        _sprite.LayerSetRsiState((splatter.Owner, sprite), 0, splatter.Comp.State);
        Face(sprite, splatter.Comp.Direction);
        _sprite.SetOffset((splatter.Owner, sprite), Vector2.Zero);
    }

    private void Apply(Entity<WolfmedBloodSplatComponent> splat)
    {
        if (!TryComp(splat, out SpriteComponent? sprite))
            return;

        _sprite.SetColor((splat.Owner, sprite), splat.Comp.Color);
        _sprite.LayerSetRsiState((splat.Owner, sprite), 0, splat.Comp.State);
        Face(sprite, splat.Comp.Direction);
    }

    /// <summary>
    /// Points a directional state without turning the picture. The entity's own rotation cannot do this:
    /// the direction is chosen in the frame of the grid it landed on, not of the camera.
    /// </summary>
    private static void Face(SpriteComponent sprite, Direction direction)
    {
        sprite.DirectionOverride = direction;
        sprite.EnableDirectionOverride = true;
    }
}
