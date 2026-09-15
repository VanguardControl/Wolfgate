using Content.Shared._Onyx.Targeting;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Robust.Shared.Configuration;

namespace Content.Server._WF.Wolfmed.Explosion;

/// <summary>Routes explosion damage on wound hosts through the distributed per-part split instead of one flat body hit.</summary>
public sealed class WolfmedExplosionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;

    private float _variation;
    private float _multiplier;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        // Both CVars have shipped unconsumed since phase 1; this is their first and only reader.
        Subs.CVar(_cfg, CCVars.ExplosionLimbDamageVariation, value => _variation = value, true);
        Subs.CVar(_cfg, CCVars.ExplosionWoundMultiplier, value => _multiplier = value, true);
    }

    /// <summary>True when the blast was spread across a wound host's parts; false means the caller should fall through.</summary>
    public bool TryApplyExplosionDamage(EntityUid entity, DamageSpecifier damage)
    {
        if (!HasComp<WoundHostComponent>(entity))
            return false;

        return _routing.TryRouteDistributedDamage(entity,
            damage,
            TargetBodyPart.All,
            DamageDistribution.SplitWithVariation,
            ignoreResistances: true,
            interruptsDoAfters: false,
            variation: _variation,
            isExplosion: true,
            woundSeverityMultiplier: _multiplier,
            originFlag: DamageableSystem.DamageOriginFlag.Explosion);
    }
}
