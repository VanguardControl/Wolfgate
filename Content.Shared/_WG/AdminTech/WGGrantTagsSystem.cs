using Content.Shared.Clothing.Components;
using Content.Shared.Inventory.Events;
using Content.Shared.Tag;

namespace Content.Shared._WG.AdminTech;

/// <summary>Adds and removes the tags of <see cref="WGGrantTagsComponent"/> as the clothing goes on and comes off.</summary>
public sealed partial class WGGrantTagsSystem : EntitySystem
{
    [Dependency] private TagSystem _tags = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGGrantTagsComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<WGGrantTagsComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnEquipped(Entity<WGGrantTagsComponent> ent, ref GotEquippedEvent args)
    {
        if (!TryComp<ClothingComponent>(ent, out var clothing) || !clothing.Slots.HasFlag(args.SlotFlags))
            return;

        EnsureComp<TagComponent>(args.Equipee);
        foreach (var tag in ent.Comp.Tags)
        {
            if (_tags.HasTag(args.Equipee, tag))
                continue;

            _tags.AddTag(args.Equipee, tag);
            ent.Comp.Given.Add(tag);
        }
    }

    private void OnUnequipped(Entity<WGGrantTagsComponent> ent, ref GotUnequippedEvent args)
    {
        foreach (var tag in ent.Comp.Given)
        {
            _tags.RemoveTag(args.Equipee, tag);
        }

        ent.Comp.Given.Clear();
    }
}
