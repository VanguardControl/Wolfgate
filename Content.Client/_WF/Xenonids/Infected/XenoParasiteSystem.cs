// XENOPORT-STUB: the concrete client system of SharedXenoParasiteSystem; RMC keeps it in Infected. Keep exactly one subclass.
using Content.Shared._WF.RMCCore.Sprite;
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Leap;
using Content.Shared._WF.Xenonids.Parasite;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Robust.Client.GameObjects;
using DrawDepthTag = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WF.Xenonids.Infected;

public sealed partial class XenoParasiteSystem : SharedXenoParasiteSystem
{
    [Dependency] private XenoVisualizerSystem _xenoVisualizer = default!;
    [Dependency] private TagSystem _tags = default!;

    public override void Initialize()
    {
        base.Initialize();

        // WOLFGATE: RMC's draw order against XenoHideSystem is dropped until the Hide lane lands
        SubscribeLocalEvent<XenoParasiteComponent, GetDrawDepthEvent>(OnGetParasiteDrawDepth);
    }

    private void OnGetParasiteDrawDepth(Entity<XenoParasiteComponent> parasite, ref GetDrawDepthEvent args)
    {
        if (_tags.HasTag(parasite, ParasiteIsPreparingLeapProtoID) ||
            HasComp<XenoLeapingComponent>(parasite))
        {
            args.DrawDepth = DrawDepthTag.Overdoors;
        }
        else
        {
            args.DrawDepth = DrawDepthTag.Mobs;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<XenoComponent, ThrownItemComponent, SpriteComponent, AppearanceComponent>();
        while (query.MoveNext(out var uid, out _, out var thrown, out var sprite, out var appearance))
        {
            _xenoVisualizer.UpdateSprite((uid, sprite, null, appearance, null, thrown));
        }
    }
}
