using Content.Server._WG.AdminObserver;
using Content.Shared._WG.AdminObserver;
using Content.Shared._WG.AdminTech;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory.Events;
using Robust.Server.Player;
using Robust.Shared.Player;

namespace Content.Server._WG.AdminTech;

/// <summary>
/// Puts the staff marker on whoever wears a staff uniform. The wording, colour and sigil come from the wearer's
/// player, as they do for a manifested admin observer; a body nobody is in reads "Staff".
/// </summary>
public sealed partial class WGStaffUniformSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private WGAdminObserverStore _store = default!;
    [Dependency] private WGStaffMarkerSystem _marker = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGStaffUniformComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<WGStaffUniformComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<WGStaffMarkerComponent, PlayerAttachedEvent>(OnPlayerAttached);
    }

    private void OnEquipped(Entity<WGStaffUniformComponent> ent, ref GotEquippedEvent args)
    {
        if (!TryComp<ClothingComponent>(ent, out var clothing) || !clothing.Slots.HasFlag(args.SlotFlags))
            return;

        // Whatever marked the wearer first, such as manifesting, keeps the marker.
        if (HasComp<WGStaffMarkerComponent>(args.Equipee))
            return;

        Mark(args.Equipee, _players.TryGetSessionByEntity(args.Equipee, out var session) ? session : null);
        ent.Comp.Marked = true;
    }

    private void OnUnequipped(Entity<WGStaffUniformComponent> ent, ref GotUnequippedEvent args)
    {
        if (!ent.Comp.Marked)
            return;

        ent.Comp.Marked = false;
        RemComp<WGStaffMarkerComponent>(args.Equipee);
    }

    private void OnPlayerAttached(Entity<WGStaffMarkerComponent> ent, ref PlayerAttachedEvent args)
    {
        // A manifested observer is marked by manifesting, with its avatar in mind.
        if (!HasComp<WGManifestedComponent>(ent))
            Mark(ent, args.Player);
    }

    private void Mark(EntityUid wearer, ICommonSession? player)
    {
        if (player == null)
            _marker.Mark(wearer, new WGStaffMarkerSettings { Wording = WGMarkerWording.Staff }, null, string.Empty, false);
        else
            _marker.Mark(wearer, player, _store.Get(player)?.Marker ?? new WGStaffMarkerSettings());
    }
}
