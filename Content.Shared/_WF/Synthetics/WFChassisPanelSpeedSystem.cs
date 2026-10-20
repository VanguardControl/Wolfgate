using Content.Shared.Movement.Systems;

namespace Content.Shared._WF.Synthetics;

/// <summary>
/// The chassis's speed modifiers: overclocked servos and an open bay. Shared so the client predicts the same speed.
/// The panel and the bay set the flags and refresh; this is the one place either is applied.
/// </summary>
public sealed class WFChassisPanelSpeedSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFChassisComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<WFChassisComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Overclocked)
            args.ModifySpeed(ent.Comp.OverclockSpeed, ent.Comp.OverclockSpeed);

        if (ent.Comp.BayOpen)
            args.ModifySpeed(ent.Comp.BayOpenSpeed, ent.Comp.BayOpenSpeed);
    }
}
