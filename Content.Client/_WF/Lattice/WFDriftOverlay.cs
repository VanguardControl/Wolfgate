using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Lattice;

/// <summary>Warps the screen as the local player's Drift climbs, from about a third of the way up.</summary>
public sealed partial class WFDriftOverlay : Overlay
{
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Drift at which the warping starts.</summary>
    private const float StartDrift = 30f;

    /// <summary>The strongest the warping gets, in the drunk shader's terms.</summary>
    private const float MaxPower = 0.45f;

    private const float PowerPerDrift = 1f / 150f;

    /// <summary>Share of the gap to the target closed each frame, so steps in Drift fade in.</summary>
    private const float Ease = 0.02f;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;
    private float _power;

    public WFDriftOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index<ShaderPrototype>("Drunk").InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var drift = _systems.GetEntitySystem<WFLatticeHallucinationSystem>().LocalLevel() * WFDriftComponent.LevelStep;
        var target = Math.Clamp((drift - StartDrift) * PowerPerDrift, 0f, MaxPower);
        _power += (target - _power) * Ease;
        return _power > 0.005f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("boozePower", _power);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
