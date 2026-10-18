// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared.Damage;

namespace Content.Shared._WF.Xenonids;

public sealed class XenoSystem : EntitySystem
{
    public DamageSpecifier TryApplyXenoAcidDamageMultiplier(EntityUid target, DamageSpecifier baseDamage)
    {
        return baseDamage;
    }
}
