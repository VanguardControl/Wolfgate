using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Emp;
using Content.Shared.Examine;
using Content.Shared.Inventory.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared._WG.Proteans;

/// <summary>A Protean's body rules: no surgery, no hardsuits, an EMP-proof seed and the slowdown of running thin.</summary>
public sealed class WGProteanSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TagSystem _tags = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGProteanComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGProteanComponent, IsEquippingAttemptEvent>(OnEquipAttempt);
        SubscribeLocalEvent<WGProteanComponent, GetVerbsEvent<InteractionVerb>>(OnInteractionVerbs);
        SubscribeLocalEvent<WGProteanComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WGProteanSeedComponent, EmpAttemptEvent>(OnSeedEmpAttempt);
        SubscribeLocalEvent<WGNanomassComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<WGNanomassComponent, AfterAutoHandleStateEvent>(OnNanomassState);
    }

    /// <summary>Every surgery and the scalpel's verb need the body to be a surgery target; a Protean isn't one.</summary>
    private void OnMapInit(Entity<WGProteanComponent> ent, ref MapInitEvent args)
    {
        RemComp<SurgeryTargetComponent>(ent);
    }

    private void OnEquipAttempt(Entity<WGProteanComponent> ent, ref IsEquippingAttemptEvent args)
    {
        // Also raised on the one dressing someone else.
        if (args.Cancelled || args.EquipTarget != ent.Owner || !IsHardsuitPiece(ent, args.Equipment))
            return;

        args.Reason = ent.Comp.HardsuitRefuse;
        args.Cancel();
    }

    /// <summary>A hardsuit, or a helmet or other piece attached to one.</summary>
    public bool IsHardsuitPiece(Entity<WGProteanComponent> ent, EntityUid item)
    {
        if (_tags.HasTag(item, ent.Comp.HardsuitTag))
            return true;

        return TryComp(item, out AttachedClothingComponent? attached)
               && Exists(attached.AttachedUid)
               && _tags.HasTag(attached.AttachedUid, ent.Comp.HardsuitTag);
    }

    /// <summary>A surgical tool on a Protean offers the usual verb, which finds nothing to cut.</summary>
    private void OnInteractionVerbs(Entity<WGProteanComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.Using is not { } tool || !HasComp<SurgeryToolComponent>(tool))
            return;

        var user = args.User;
        var target = ent.Owner;
        var message = ent.Comp.SurgeryRefuse;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("surgery-verb-text"),
            Message = Loc.GetString("surgery-verb-message"),
            Icon = new SpriteSpecifier.Rsi(new ResPath("/Textures/Objects/Specific/Medical/Surgery/scalpel.rsi"), "scalpel"),
            Act = () => _popup.PopupClient(Loc.GetString(message), target, user),
        });
    }

    /// <summary>How loosely the body holds its shape, by the damage it carries.</summary>
    private void OnExamined(Entity<WGProteanComponent> ent, ref ExaminedEvent args)
    {
        if (!TryComp(ent, out DamageableComponent? damageable))
            return;

        var damage = damageable.TotalDamage.Float();
        LocId? line = damage >= ent.Comp.HurtHeavy ? "wg-protean-examine-hurt-heavy"
            : damage >= ent.Comp.HurtMedium ? "wg-protean-examine-hurt-medium"
            : damage >= ent.Comp.HurtLight ? "wg-protean-examine-hurt-light"
            : null;
        if (line is { } shown)
            args.PushMarkup(Loc.GetString(shown));
    }

    private void OnSeedEmpAttempt(Entity<WGProteanSeedComponent> ent, ref EmpAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnRefreshSpeed(Entity<WGNanomassComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Thin)
            args.ModifySpeed(ent.Comp.ThinSpeed, ent.Comp.ThinSpeed);
    }

    private void OnNanomassState(Entity<WGNanomassComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _speed.RefreshMovementSpeedModifiers(ent);
    }
}
