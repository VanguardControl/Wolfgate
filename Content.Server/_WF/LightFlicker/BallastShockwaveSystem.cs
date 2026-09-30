using Content.Shared._WF.CCVar;
using Content.Shared._WF.Explosion;
using Content.Shared._WF.LightFlicker;
using Content.Shared.Light.Components;
using Robust.Shared.Configuration;

namespace Content.Server._WF.LightFlicker;

/// <summary>
/// Damages the ballast of every powered light an explosion's shockwave reaches, the same reach as the ring and shove.
/// </summary>
public sealed class BallastShockwaveSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
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

        if (reach <= 0f)
            return;

        _lights.Clear();
        _lookup.GetEntitiesInRange(args.Epicenter, reach, _lights);

        foreach (var light in _lights)
        {
            _ballast.DamageBallast(light);
        }

        _lights.Clear();
    }
}
