using Content.Shared._WG.MentorCritters;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.MentorCritters;

/// <summary>Draws mentor critters amber with a soft one-pixel halo, so nobody takes one for a real animal.</summary>
public sealed class WGMentorCritterVisualsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public const string HaloPass = "wg-mentor-critter-halo";

    /// <summary>Moon's picks: the amber tint and a soft halo.</summary>
    public static readonly Color Amber = Color.FromHex("#FFC266");

    private static readonly ProtoId<ShaderPrototype> HaloShader = "ShieldingOutline";
    private const float HaloWidth = 1f;

    private readonly Dictionary<EntityUid, ShaderInstance> _halos = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMentorCritterComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WGMentorCritterComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WGMentorCritterComponent, BeforePostShaderRenderEvent>(OnBeforeRender);
    }

    private void OnStartup(EntityUid uid, WGMentorCritterComponent comp, ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        _sprite.SetColor((uid, sprite), Amber);

        var halo = _prototypes.Index(HaloShader).InstanceUnique();
        halo.SetParameter("outline_color", Amber.WithAlpha(0.6f));
        halo.SetParameter("outline_fullbright", true);
        _halos[uid] = halo;
        _sprite.SetPostShader((uid, sprite), new SpriteComponent.PostShaderArgs(HaloPass, halo) { RaiseShaderEvent = true });
    }

    private void OnShutdown(EntityUid uid, WGMentorCritterComponent comp, ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
            _sprite.RemovePostShader((uid, sprite), HaloPass);

        if (_halos.Remove(uid, out var halo))
            halo.Dispose();
    }

    private void OnBeforeRender(EntityUid uid, WGMentorCritterComponent comp, ref BeforePostShaderRenderEvent args)
    {
        // The pass works in the viewport's pixels, so a sharper viewport needs a wider halo to look the same.
        if (args.Id == HaloPass)
            args.Shader.SetParameter("outline_width", HaloWidth * MathF.Max(args.Viewport.RenderScale.X, 1f));
    }
}
