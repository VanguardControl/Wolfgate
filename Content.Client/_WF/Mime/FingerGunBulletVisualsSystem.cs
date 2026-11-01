using Content.Shared._WF.Mime;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Mime;

/// <summary>Keeps finger gun shots hidden, since gun prediction turns every predicted projectile's sprite back on.</summary>
public sealed partial class FingerGunBulletVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<FingerGunBulletComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite))
        {
            if (sprite.Visible)
                _sprite.SetVisible((uid, sprite), false);
        }
    }
}
