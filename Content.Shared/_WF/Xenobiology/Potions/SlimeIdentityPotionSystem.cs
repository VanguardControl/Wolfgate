using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects.Components.Localization;

namespace Content.Shared._WF.Xenobiology.Potions;

/// <summary>
/// The gender change and name change potions.
/// </summary>
public sealed partial class SlimeIdentityPotionSystem : EntitySystem
{
    [Dependency] private GrammarSystem _grammar = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly Gender[] Genders = { Gender.Neuter, Gender.Epicene, Gender.Female, Gender.Male };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeGenderChangePotionComponent, AfterInteractEvent>(OnGenderInteract);
        SubscribeLocalEvent<SlimeGenderChangePotionComponent, GetVerbsEvent<InteractionVerb>>(OnGenderVerbs);
        SubscribeLocalEvent<SlimeNameChangePotionComponent, AfterInteractEvent>(OnNameInteract);
        SubscribeLocalEvent<SlimeNameChangePotionComponent, SlimeNameChangePotionNewNameChangedMessage>(OnNameChanged);
    }

    private void OnGenderInteract(Entity<SlimeGenderChangePotionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !TryComp<HumanoidAppearanceComponent>(args.Target, out var humanoid))
            return;

        args.Handled = true;
        if (ent.Comp.Gender is not { } gender)
        {
            _popup.PopupClient(Loc.GetString("slime-gender-select-first"), args.User, args.User);
            return;
        }

        var key = gender.ToString().ToLowerInvariant();
        if (humanoid.Gender == gender)
        {
            _popup.PopupClient(Loc.GetString($"comp-gender-change-potion-{key}-set-already"), args.User, args.User);
            return;
        }

        var target = args.Target.Value;
        humanoid.Gender = gender;
        Dirty(target, humanoid);
        if (TryComp<GrammarComponent>(target, out var grammar))
            _grammar.SetGender((target, grammar), gender);

        _popup.PopupClient(Loc.GetString($"comp-gender-change-potion-{key}-set"), args.User, args.User);
        PredictedQueueDel(ent.Owner);
    }

    private void OnGenderVerbs(Entity<SlimeGenderChangePotionComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var category = new VerbCategory(Loc.GetString("comp-gender-change-potion-category"), null);
        foreach (var gender in Genders)
        {
            var key = gender.ToString().ToLowerInvariant();
            var picked = ent.Comp.Gender == gender;
            args.Verbs.Add(new InteractionVerb
            {
                Text = Loc.GetString($"comp-gender-change-potion-{key}"),
                Message = Loc.GetString(picked
                    ? $"comp-gender-change-potion-{key}-set-already"
                    : $"comp-gender-change-potion-{key}-set"),
                Disabled = picked,
                Category = category,
                Act = () =>
                {
                    ent.Comp.Gender = gender;
                    Dirty(ent);
                },
            });
        }
    }

    private void OnNameInteract(Entity<SlimeNameChangePotionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<MindContainerComponent>(target))
            return;

        args.Handled = true;
        var newName = ent.Comp.AssignedName.Trim();
        if (newName.Length == 0)
            return;

        var oldName = Name(target);
        _metaData.SetEntityName(target, newName);
        if (args.User != target)
            _popup.PopupClient(Loc.GetString("slime-potion-name-changed", ("old", oldName), ("new", newName)), args.User, args.User);
        _popup.PopupEntity(Loc.GetString("slime-potion-name-changed-self", ("new", newName)), target, target);
        PredictedQueueDel(ent.Owner);
    }

    private void OnNameChanged(Entity<SlimeNameChangePotionComponent> ent, ref SlimeNameChangePotionNewNameChangedMessage args)
    {
        ent.Comp.AssignedName = args.NewName.Length > MaxNameLength ? args.NewName[..MaxNameLength] : args.NewName;
        Dirty(ent);
    }

    /// <summary>
    /// Longest name the potion takes.
    /// </summary>
    public const int MaxNameLength = 32;
}
