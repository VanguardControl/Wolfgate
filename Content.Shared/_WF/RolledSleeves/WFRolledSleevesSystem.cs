using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared._WF.RolledSleeves;

/// <summary>Adds the roll up / roll down sleeves verb to jumpsuits.</summary>
public sealed partial class WFRolledSleevesSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFRolledSleevesComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(Entity<WFRolledSleevesComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        // Only the wearer rolls the sleeves of a worn suit.
        if (_inventory.TryGetContainingEntity(ent.Owner, out var wearer) && wearer != args.User)
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(ent.Comp.Rolled ? "wf-rolled-sleeves-verb-unroll" : "wf-rolled-sleeves-verb-roll"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/fold.svg.192dpi.png")),
            Act = () => Toggle(ent, user),
        });
    }

    /// <summary>Rolls the sleeves up or back down.</summary>
    public void Toggle(Entity<WFRolledSleevesComponent> ent, EntityUid user)
    {
        ent.Comp.Rolled = !ent.Comp.Rolled;
        Dirty(ent);
        _item.VisualsChanged(ent);

        var msg = ent.Comp.Rolled ? "wf-rolled-sleeves-popup-roll" : "wf-rolled-sleeves-popup-unroll";
        _popup.PopupClient(Loc.GetString(msg, ("item", ent.Owner)), user, user);
    }
}
