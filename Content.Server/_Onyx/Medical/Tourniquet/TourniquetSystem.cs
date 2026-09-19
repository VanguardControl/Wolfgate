using System.Linq;
using Content.Shared._Shitmed.Targeting; // WOLFGATE: D10 — Onyx's TargetingComponent registers as "Targeting", colliding with Shitmed's; use Shitmed's identical field instead
using Content.Shared._WF.Wolfmed.Targeting; // WOLFGATE: D10 — WoundTargetResolver replaces the absent TargetResolverSystem
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE: W2 — arterial bleeds decide where a tourniquet helps
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._Onyx.Medical.Tourniquet;

public sealed partial class TourniquetSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WoundTargetResolver _targeting = default!; // WOLFGATE: D10 — TargetResolverSystem is absent; signature-exact TryResolveExact replacement
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!; // WOLFGATE (W2): which bleeds can be tied off
    [Dependency] private WoundDamageRoutingSystem _damage = default!;
    [Dependency] private WoundSystem _wounds = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TourniquetComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<TourniquetComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<TourniquetComponent, TourniquetDoAfterEvent>(OnDoAfter);
    }

    private void OnUseInHand(Entity<TourniquetComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStart(ent, args.User, args.User);
    }

    private void OnAfterInteract(Entity<TourniquetComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        args.Handled = TryStart(ent, target, args.User);
    }

    private bool TryStart(Entity<TourniquetComponent> tourniquet, EntityUid body, EntityUid user)
    {
        if (!TryComp(user, out TargetingComponent? targeting) ||
            !_targeting.TryResolveExact(body, targeting.Target, out var part))
        {
            _popup.PopupEntity(Loc.GetString("tourniquet-selected-part-missing"), body, user);
            return false;
        }

        if (!CanApply(body, part))
        {
            // WOLFGATE (W2): an arterial bleed away from the limbs has nowhere to tie off; say so.
            _popup.PopupEntity(Loc.GetString(_bleeding.GetPartRate(part) > 0f
                ? "wolfmed-tourniquet-nowhere-to-tie"
                : "tourniquet-no-bleeding"), body, user);
            return false;
        }

        _audio.PlayPredicted(tourniquet.Comp.BeginSound, body, user);
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            user,
            tourniquet.Comp.Delay,
            new TourniquetDoAfterEvent(GetNetEntity(part)),
            tourniquet,
            body,
            tourniquet)
        {
            NeedHand = true,
            BreakOnMove = true,
        });
    }

    private void OnDoAfter(Entity<TourniquetComponent> tourniquet, ref TourniquetDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Args.Target is not { } body)
            return;

        args.Handled = true;
        var part = GetEntity(args.Part);
        if (!Apply(body, part))
            return;

        if (!tourniquet.Comp.Damage.Empty)
            _damage.TryApplyPartDamage(body, part, tourniquet.Comp.Damage, args.Args.User);
        _audio.PlayPredicted(tourniquet.Comp.EndSound, body, args.Args.User);
        _popup.PopupEntity(Loc.GetString("tourniquet-applied"), body, args.Args.User);
        QueueDel(tourniquet); // WOLFGATE: D13 — class is now Content.Server-only, so Onyx's "if (_net.IsServer)" guard is always true; dropped with the INetManager dependency
    }

    public bool Apply(EntityUid body, EntityUid part)
    {
        if (!CanApply(body, part)) // WOLFGATE: D13 — class is now Content.Server-only, so Onyx's "!_net.IsServer ||" half of this guard is always false; dropped with the INetManager dependency
            return false;

        var applied = false;
        foreach (var wound in _wounds.GetWounds((part, Comp<WoundableComponent>(part))).ToArray())
        {
            if (!TryComp(wound, out WoundBleedingComponent? bleeding) || bleeding.CurrentRate <= 0f ||
                !_traits.CanTourniquet(wound.Owner, part)) // WOLFGATE (W2): a torso or head artery cannot be tied off.
                continue;

            applied |= _bleeding.SetTreatment(wound.Owner, BleedingTreatment.Clamped);
        }

        return applied;
    }

    private bool CanApply(EntityUid body, EntityUid part) =>
        _body.BodyHasChild(body, part) && HasComp<WoundableComponent>(part) && _bleeding.GetPartRate(part) > 0f &&
        _traits.CanTourniquetPart(part); // WOLFGATE (W2): nothing to do if every bleed here is untieable.
}
