using Content.Shared.Administration;
using Content.Shared.Administration.Managers;
using Content.Shared.Examine;
using Content.Shared.Inventory.Events;

namespace Content.Shared._WG.AdminTech;

/// <summary>Keeps administrative gear on its wearer and labels it on examine.</summary>
public sealed partial class WGAdminGearSystem : EntitySystem
{
    [Dependency] private ISharedAdminManager _admin = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGAdminGearComponent, BeingUnequippedAttemptEvent>(OnUnequipAttempt);
        SubscribeLocalEvent<WGAdminGearComponent, ExaminedEvent>(OnExamined);
    }

    private void OnUnequipAttempt(Entity<WGAdminGearComponent> ent, ref BeingUnequippedAttemptEvent args)
    {
        if (args.Cancelled || args.Unequipee == args.UnEquipTarget || _admin.HasAdminFlag(args.Unequipee, AdminFlags.Admin))
            return;

        args.Reason = "wg-admin-gear-strip-blocked";
        args.Cancel();
    }

    private void OnExamined(Entity<WGAdminGearComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-admin-gear-examine"));
    }
}
