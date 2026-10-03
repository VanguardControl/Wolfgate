using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Shares sightings only with crew whose equipped radios actually receive the report.</summary>
public sealed class WFCrewCommsSystem : EntitySystem
{
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    private (EntityUid Sender, EntityUid Target, EntityUid Radio, WFRadioLine? Incident)? _report;
    private readonly Dictionary<(EntityUid Sender, WFRadioLine Line), TimeSpan> _incidents = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewRadioComponent, RadioReceiveEvent>(OnReceive);
    }

    /// <summary>Whether a recent sighting reached this crew member over radio.</summary>
    public bool Knows(EntityUid uid, EntityUid target) => TryComp<WFCrewComponent>(uid, out var crew)
        && crew.RadioSightings.TryGetValue(target, out var until) && _timing.CurTime < until;

    /// <summary>Relays a witnessed contact or incident over Shortband, with ten-second report cooldowns.</summary>
    public void Report(EntityUid sender, EntityUid target, WFRadioLine? incident = null)
    {
        if (!TryComp<WFCrewComponent>(sender, out var crew) || !crew.ShareAlerts || !_mobs.IsAlive(sender)
            || incident == null && (_timing.CurTime < crew.NextReport
                || !EntityManager.System<WFCrewSecuritySystem>().IsBoardingCandidate(target))
            || !_inventory.TryGetSlotEntity(sender, "ears", out var headset)
            || !TryComp<HeadsetComponent>(headset, out var transmitter) || !transmitter.Enabled
            || !HasComp<WFCrewRadioComponent>(headset) || !TryComp<ActiveRadioComponent>(headset, out var active)
            || !active.Channels.Contains("Traffic"))
            return;
        foreach (var key in _incidents.Where(entry => entry.Value <= _timing.CurTime).Select(entry => entry.Key).ToArray())
            _incidents.Remove(key);
        if (incident is { } kind)
        {
            if (_incidents.ContainsKey((sender, kind)))
                return;
            _incidents[(sender, kind)] = _timing.CurTime + TimeSpan.FromSeconds(10);
        }
        crew.NextReport = _timing.CurTime + TimeSpan.FromSeconds(10);
        var previous = _report;
        _report = (sender, target, headset.Value, incident);
        try
        {
            var line = incident switch
            {
                WFRadioLine.Mayday => "wf-crew-radio-contact-under-fire",
                WFRadioLine.CaptainDown or WFRadioLine.HelmDown => "wf-crew-radio-contact-down",
                _ => "wf-crew-radio-contact",
            };
            _radio.SendRadioMessage(sender, Loc.GetString(line, ("target", Name(target))), "Traffic", headset.Value);
        }
        finally
        {
            _report = previous;
        }
    }

    private void OnReceive(Entity<WFCrewRadioComponent> ent, ref RadioReceiveEvent args)
    {
        if (_report is not { } report || args.MessageSource != report.Sender || args.RadioSource != report.Radio
            || args.Channel.ID != "Traffic"
            || !TryComp<HeadsetComponent>(ent, out var receiver) || !receiver.Enabled
            || !TryComp<WFCrewComponent>(report.Sender, out var sender))
            return;
        var wearer = Transform(ent).ParentUid;
        if (!TryComp<WFCrewComponent>(wearer, out var crew) || !crew.ShareAlerts || !_mobs.IsAlive(wearer)
            || crew.Group != sender.Group
            || Transform(wearer).GridUid != Transform(report.Sender).GridUid
            || !_inventory.TryGetSlotEntity(wearer, "ears", out var equipped) || equipped != ent.Owner)
            return;
        if (report.Incident is { } incident)
            EntityManager.System<WFRadioOperatorSystem>().ReceiveIncident(wearer, report.Target, incident);
        if (report.Incident is WFRadioLine.CaptainDown or WFRadioLine.HelmDown)
            return;
        crew.RadioSightings[report.Target] = _timing.CurTime + TimeSpan.FromSeconds(30);
        EntityManager.System<WFCrewSecuritySystem>().ReceiveSighting(wearer, report.Target);
    }
}
