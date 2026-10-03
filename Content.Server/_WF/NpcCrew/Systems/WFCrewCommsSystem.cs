using Content.Server._WF.NpcCrew.Components;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Radio.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Shares sightings only with crew whose equipped radios actually receive the report.</summary>
public sealed class WFCrewCommsSystem : EntitySystem
{
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IGameTiming _timing = default!;
    private (EntityUid Sender, EntityUid Target)? _report;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewRadioComponent, RadioReceiveEvent>(OnReceive);
    }

    /// <summary>Whether a recent sighting reached this crew member over radio.</summary>
    public bool Knows(EntityUid uid, EntityUid target) => TryComp<WFCrewComponent>(uid, out var crew)
        && crew.RadioSightings.TryGetValue(target, out var until) && _timing.CurTime < until;

    /// <summary>Reports a visible contact over Shortband, with a shared ten-second transmission cooldown.</summary>
    public void Report(EntityUid sender, EntityUid target)
    {
        if (!TryComp<WFCrewComponent>(sender, out var crew) || !crew.ShareAlerts || _timing.CurTime < crew.NextReport
            || !_inventory.TryGetSlotEntity(sender, "ears", out var headset)
            || !HasComp<WFCrewRadioComponent>(headset) || !HasComp<ActiveRadioComponent>(headset))
            return;
        crew.NextReport = _timing.CurTime + TimeSpan.FromSeconds(10);
        _report = (sender, target);
        try
        {
            _radio.SendRadioMessage(sender, Loc.GetString("wf-crew-radio-contact", ("target", Name(target))), "Traffic", headset.Value);
        }
        finally
        {
            _report = null;
        }
    }

    private void OnReceive(Entity<WFCrewRadioComponent> ent, ref RadioReceiveEvent args)
    {
        if (_report is not { } report || args.MessageSource != report.Sender || args.Channel.ID != "Traffic"
            || !TryComp<WFCrewComponent>(report.Sender, out var sender))
            return;
        var wearer = Transform(ent).ParentUid;
        if (!TryComp<WFCrewComponent>(wearer, out var crew) || !crew.ShareAlerts || crew.Group != sender.Group
            || Transform(wearer).GridUid != Transform(report.Sender).GridUid
            || !_inventory.TryGetSlotEntity(wearer, "ears", out var equipped) || equipped != ent.Owner)
            return;
        crew.RadioSightings[report.Target] = _timing.CurTime + TimeSpan.FromSeconds(30);
        EntityManager.System<WFCrewSecuritySystem>().ReceiveSighting(wearer, report.Target);
    }
}
