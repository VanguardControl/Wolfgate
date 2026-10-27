using Content.Server.Ghost.Roles.Components;
using Content.Server.Mind.Commands;
using Content.Server.Popups;
using Content.Shared.Atmos;
using Content.Shared.Clothing.Components;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.Radio.Components;
using Content.Shared.Temperature;
using Content.Shared._EinsteinEngines.Language.Components;
using Content.Shared._WF.Xenobiology.Potions;

namespace Content.Server._WF.Xenobiology.Potions;

/// <summary>
/// Clothing a fireproof potion was used on: no fire damage or heating through it.
/// </summary>
[RegisterComponent]
public sealed partial class SlimeFireproofedComponent : Component;

/// <summary>
/// The potions that touch server-only parts: fireproof, sentience and bluespace radio.
/// </summary>
public sealed partial class SlimeServerPotionSystem : EntitySystem
{
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeFireproofPotionComponent, AfterInteractEvent>(OnFireproof);
        SubscribeLocalEvent<SlimeSentiencePotionComponent, AfterInteractEvent>(OnSentience);
        SubscribeLocalEvent<SlimeBluespaceRadioPotionComponent, AfterInteractEvent>(OnRadio);

        SubscribeLocalEvent<SlimeFireproofedComponent, InventoryRelayedEvent<GetFireProtectionEvent>>(OnGetFireProtection);
        SubscribeLocalEvent<SlimeFireproofedComponent, InventoryRelayedEvent<ModifyChangedTemperatureEvent>>(OnTemperatureChange);
    }

    private void OnFireproof(Entity<SlimeFireproofPotionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<ClothingComponent>(target))
            return;

        args.Handled = true;
        if (HasComp<SlimeFireproofedComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("slime-fireproof-max"), args.User, args.User);
            return;
        }

        AddComp<SlimeFireproofedComponent>(target);
        ent.Comp.RemainingUses--;
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("slime-fireproof-applied", ("uses", ent.Comp.RemainingUses)), args.User, args.User);
        if (ent.Comp.RemainingUses <= 0)
            QueueDel(ent);
    }

    private void OnGetFireProtection(Entity<SlimeFireproofedComponent> ent, ref InventoryRelayedEvent<GetFireProtectionEvent> args)
    {
        args.Args.Multiplier = 0f;
    }

    private void OnTemperatureChange(Entity<SlimeFireproofedComponent> ent, ref InventoryRelayedEvent<ModifyChangedTemperatureEvent> args)
    {
        if (args.Args.TemperatureDelta > 0)
            args.Args.TemperatureDelta = 0;
    }

    private void OnSentience(Entity<SlimeSentiencePotionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        args.Handled = true;
        if (HasComp<MindContainerComponent>(target) || HasComp<GhostRoleComponent>(target))
            return;

        MakeSentientCommand.MakeSentient(target, EntityManager);
        var ghostRole = AddComp<GhostRoleComponent>(target);
        EnsureComp<GhostTakeoverAvailableComponent>(target);
        ghostRole.RoleName = Name(target);
        ghostRole.RoleDescription = Loc.GetString("ghost-role-information-slime-sentience-potion-description");
        QueueDel(ent);
    }

    private void OnRadio(Entity<SlimeBluespaceRadioPotionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<LanguageSpeakerComponent>(target))
            return;

        args.Handled = true;
        var active = EnsureComp<ActiveRadioComponent>(target);
        var transmitter = EnsureComp<IntrinsicRadioTransmitterComponent>(target);
        EnsureComp<IntrinsicRadioReceiverComponent>(target);
        foreach (var channel in ent.Comp.Channels)
        {
            active.Channels.Add(channel);
            transmitter.Channels.Add(channel);
        }

        _popup.PopupEntity(Loc.GetString("slime-potion-radio-applied", ("target", target)), target, target);
        QueueDel(ent);
    }
}
