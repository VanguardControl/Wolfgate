using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;

namespace Content.Shared._WF.Wolfmed.Body;

/// <summary>Reads Wolfmed part data off Shitmed body parts, with a no-wounds default.</summary>
public sealed class WolfmedBodyPartSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;

    private static readonly WolfmedBodyPartComponent None = new();

    /// <summary>Wolfmed data for a part; a zeroed default when the part has none.</summary>
    public WolfmedBodyPartComponent Get(EntityUid part) => CompOrNull<WolfmedBodyPartComponent>(part) ?? None;

    /// <summary>
    /// Trims AMBIENT part damage (the caller only sends damage with no attacker behind it) so the body's total never
    /// passes the ceiling. Attacks and explosions are never trimmed: limbs have to be able to come off a corpse. Routed damage has no natural one (ten
    /// parts, no per-limb cap), so a burning corpse climbed to its 1500 gib threshold. The ceiling is absolute,
    /// not a multiple of the dead threshold: an IPC dies at 100 but its limbs only come off near 200 each.
    /// </summary>
    public void ClampToBodyCap(EntityUid body, DamageSpecifier damage)
    {
        var cap = _config.GetCVar(WolfmedCVars.BodyDamageCap);
        if (cap <= 0f || !TryComp(body, out DamageableComponent? damageable))
            return;

        var incoming = FixedPoint2.Zero;
        foreach (var amount in damage.DamageDict.Values)
        {
            if (amount > FixedPoint2.Zero)
                incoming += amount;
        }

        if (incoming <= FixedPoint2.Zero)
            return;

        var remaining = FixedPoint2.Max(FixedPoint2.New(cap) - damageable.TotalDamage, FixedPoint2.Zero);
        if (incoming <= remaining)
            return;

        var scale = remaining.Float() / incoming.Float();
        foreach (var (type, amount) in new Dictionary<string, FixedPoint2>(damage.DamageDict))
        {
            if (amount <= FixedPoint2.Zero)
                continue;

            var kept = amount * scale;
            if (kept <= FixedPoint2.Zero)
                damage.DamageDict.Remove(type);
            else
                damage.DamageDict[type] = kept;
        }
    }
}
