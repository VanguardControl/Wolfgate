using Content.Shared._WF.CCVar;
using Content.Shared._WF.Explosion;
using Content.Shared._WF.LightFlicker;
using Content.Shared.Light.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Server._WF.LightFlicker;

/// <summary>
/// Gives each powered light an explosion's shockwave reaches (the same reach as the ring and shove) a chance of a
/// damaged ballast.
/// </summary>
public sealed class BallastShockwaveSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private LightBallastSystem _ballast = default!;

    private readonly HashSet<Entity<PoweredLightComponent>> _lights = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ExplosionShockwaveEvent>(OnShockwave);
    }

    private void OnShockwave(ref ExplosionShockwaveEvent args)
    {
        var reach = args.Iterations + _cfg.GetCVar(ShockwaveCVars.Overshoot);
        var chance = Math.Clamp(_cfg.GetCVar(LightFlickerCVars.BallastDamageChance), 0f, 1f);

        if (reach <= 0f || chance <= 0f)
            return;

        _lights.Clear();
        _lookup.GetEntitiesInRange(args.Epicenter, reach, _lights);

        foreach (var light in _lights)
        {
            if (_random.Prob(chance))
                _ballast.DamageBallast(light);
        }

        _lights.Clear();
    }
}
