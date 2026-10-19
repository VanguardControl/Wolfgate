using Content.Server.Atmos.EntitySystems;
using Content.Server.Damage.Systems;
using Content.Shared._WF.Xenonids.FirePat;
using Content.Shared.Atmos.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenonids.FirePat;

/// <summary>
/// RMC's fire patting on upstream fire: a <see cref="XenoFirePatterComponent"/> holder clicking a burning mob with
/// an empty hand takes fire stacks off it.
/// </summary>
public sealed class XenoFirePatSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<FlammableComponent, InteractHandEvent>(OnFlammableInteractHand,
            before: [typeof(InteractionPopupSystem), typeof(DamagePopupSystem)]);
    }

    private void OnFlammableInteractHand(Entity<FlammableComponent> ent, ref InteractHandEvent args)
    {
        var user = args.User;
        if (args.Handled ||
            args.Target != ent.Owner ||
            user == ent.Owner ||
            !ent.Comp.OnFire ||
            !HasComp<MobStateComponent>(ent) ||
            !TryComp(user, out XenoFirePatterComponent? patter))
        {
            return;
        }

        args.Handled = true;
        var time = _timing.CurTime;
        if (time < patter.LastPat + patter.Cooldown)
            return;

        patter.LastPat = time;
        _flammable.AdjustFireStacks(ent, patter.Stacks, ent.Comp);

        _audio.PlayPvs(patter.Sound, user);
        _popup.PopupEntity(Loc.GetString("wf-xeno-fire-pat-user", ("target", ent.Owner)), ent, user, PopupType.SmallCaution);
        _popup.PopupEntity(Loc.GetString("wf-xeno-fire-pat-target", ("user", user)), ent, ent, PopupType.SmallCaution);

        var others = Filter.PvsExcept(ent).RemoveWhereAttachedEntity(e => e == user || e == ent.Owner);
        _popup.PopupEntity(Loc.GetString("wf-xeno-fire-pat-others", ("user", user), ("target", ent.Owner)), ent, others, true);
    }
}
