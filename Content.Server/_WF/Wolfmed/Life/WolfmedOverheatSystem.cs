using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>
/// Overheating for a wound host. Upstream sets <c>MobState.Dead</c> the moment a chassis crosses its
/// overheat threshold, which the Wolfmed model does not allow: a wound host dies through its brain, not
/// through a number. So it cooks instead. Heavy Heat damage routed through the ordinary wound pipeline
/// burns the parts, damages the organs behind them and, for an IPC, kills the pump long before the
/// positronic brain goes, which is a shutdown every BRAIN-aware system can already see coming.
/// </summary>
public sealed class WolfmedOverheatSystem : EntitySystem
{
    private static readonly TimeSpan PulseInterval = TimeSpan.FromSeconds(1);
    private static readonly ProtoId<DamageTypePrototype> Heat = "Heat";

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly WolfmedConsciousnessSystem _consciousness = default!;
    [Dependency] private readonly WolfmedDamageableSystem _damageable = default!;

    /// <summary>
    /// Takes over the kill for a body whose death Wolfmed owns, and reports whether it did. Called once a
    /// frame per overheating body, so the burn and the warning sit on their own pulse.
    /// </summary>
    public bool TryOverheat(EntityUid body, LocId popup)
    {
        if (TerminatingOrDeleted(body) || !_consciousness.OwnsMobState(body))
            return false;

        var comp = EnsureComp<WolfmedOverheatComponent>(body);
        if (comp.NextPulse > _timing.CurTime)
            return true;

        comp.NextPulse = _timing.CurTime + PulseInterval;

        _popup.PopupEntity(Loc.GetString(popup, ("name", Identity.Name(body, EntityManager))), body,
            PopupType.LargeCaution);

        var damage = new DamageSpecifier(_prototypes.Index(Heat), FixedPoint2.New(comp.HeatPerPulse));
        _damageable.ChangeDamage(body, damage);
        return true;
    }
}
