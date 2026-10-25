using Content.Shared._WF.AsteroidMining;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._WF.AsteroidMining;

/// <summary>Picks each rock's sprite variant and vein tint, and makes its cracks glow with heat under a beam.</summary>
public sealed partial class WFAsteroidVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly Color Ember = new(0.55f, 0.04f, 0f);
    private static readonly Color Orange = new(1f, 0.42f, 0.05f);
    private static readonly Color WhiteHot = new(1f, 0.92f, 0.7f);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFAsteroidComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFAsteroidComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnStartup(Entity<WFAsteroidComponent> ent, ref ComponentStartup args)
    {
        Apply(ent);
    }

    private void OnState(Entity<WFAsteroidComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Apply(ent);
    }

    private void Apply(Entity<WFAsteroidComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var variant = ent.Comp.Variant;
        _sprite.LayerSetRsiState((ent, sprite), WFAsteroidVisualLayers.Rock, $"rock{variant}");
        _sprite.LayerSetRsiState((ent, sprite), WFAsteroidVisualLayers.Veins, $"veins{variant}");
        _sprite.LayerSetRsiState((ent, sprite), WFAsteroidVisualLayers.Heat, $"heat{variant}");
        _sprite.LayerSetColor((ent, sprite), WFAsteroidVisualLayers.Veins, ent.Comp.VeinColor);
        SetHeat((ent, ent.Comp, sprite), ent.Comp.DisplayCharge);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var query = EntityQueryEnumerator<WFAsteroidComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var rock, out var sprite))
        {
            if (rock.Charge <= 0f && rock.DisplayCharge <= 0f)
                continue;

            // Ease toward the server's heat so 50 ms state steps don't show.
            rock.DisplayCharge += (rock.Charge - rock.DisplayCharge) * MathF.Min(1f, frameTime * 6f);
            if (rock.Charge <= 0f && rock.DisplayCharge < 0.005f)
                rock.DisplayCharge = 0f;
            SetHeat((uid, rock, sprite), rock.DisplayCharge);
        }
    }

    private void SetHeat(Entity<WFAsteroidComponent, SpriteComponent> ent, float charge)
    {
        var heat = Math.Clamp(charge, 0f, 1.2f);
        var color = heat < 0.55f
            ? Color.InterpolateBetween(Ember, Orange, heat / 0.55f)
            : Color.InterpolateBetween(Orange, WhiteHot, Math.Clamp((heat - 0.55f) / 0.45f, 0f, 1f));
        var alpha = Math.Clamp((heat - 0.03f) / 0.45f, 0f, 1f);
        if (ent.Comp1.Overcharging)
        {
            var time = (float) _timing.RealTime.TotalSeconds;
            alpha *= 0.65f + 0.35f * MathF.Sin(time * 27f);
        }

        _sprite.LayerSetColor((ent, ent.Comp2), WFAsteroidVisualLayers.Heat, color.WithAlpha(alpha));
        if (!TryComp<PointLightComponent>(ent, out var light))
            return;

        _lights.SetEnabled(ent, alpha > 0.02f, light);
        _lights.SetColor(ent, color, light);
        _lights.SetEnergy(ent, alpha * 2.5f, light);
    }
}
