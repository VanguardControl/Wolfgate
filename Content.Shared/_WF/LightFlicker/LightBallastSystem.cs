using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Light.Components;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;

namespace Content.Shared._WF.LightFlicker;

/// <summary>
/// Damages and repairs light ballasts. A damaged ballast is fixed with a multitool.
/// </summary>
public sealed class LightBallastSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamagedBallastComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<DamagedBallastComponent, BallastRepairDoAfterEvent>(OnRepaired);
        SubscribeLocalEvent<DamagedBallastComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>Damages the ballast of a powered light so it starts flickering.</summary>
    public void DamageBallast(EntityUid uid)
    {
        if (HasComp<PoweredLightComponent>(uid))
            EnsureComp<DamagedBallastComponent>(uid);
    }

    private void OnInteractUsing(Entity<DamagedBallastComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tool.HasQuality(args.Used, ent.Comp.RepairQuality))
            return;

        args.Handled = true;

        if (_tool.UseTool(args.Used, args.User, ent, ent.Comp.RepairTime, ent.Comp.RepairQuality, new BallastRepairDoAfterEvent()))
            _popup.PopupClient(Loc.GetString("light-ballast-repair-start"), ent, args.User);
    }

    private void OnRepaired(Entity<DamagedBallastComponent> ent, ref BallastRepairDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        RemComp<DamagedBallastComponent>(ent);
        _popup.PopupClient(Loc.GetString("light-ballast-repair-done"), ent, args.User);
    }

    private void OnExamined(Entity<DamagedBallastComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("light-ballast-examine-damaged"));
    }
}
