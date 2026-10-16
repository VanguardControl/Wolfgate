using System.Linq;
using Content.Server._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.RoundSetup;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFShipScenarioSystem
{
    [Dependency] private IComponentFactory _factionComponents = default!;

    /// <summary>
    /// TSF and PDV offer the ships on their existing T0 and T1 vouchers, including downstream additions. VG, USSP and
    /// the Underground have no tier vouchers and offer their whole shipyard group; null stands for that.
    /// </summary>
    private HashSet<string>? FactionVessels(WFCrewFaction faction)
    {
        var prefix = faction switch { WFCrewFaction.TSF => "Tsf", WFCrewFaction.PDV => "Pdv", _ => null };
        if (prefix == null)
            return null;
        var vessels = new HashSet<string>();
        foreach (var tier in new[] { 0, 1 })
        {
            if (!_prototypes.TryIndex<EntityPrototype>($"ShipVoucher{prefix}T{tier}", out var prototype) ||
                !prototype.Components.TryGetComponent<ShipyardVoucherComponent>(_factionComponents, out var voucher) ||
                voucher.CompanyName != WFFactionCrewPolicy.Company(faction))
                continue;
            foreach (var id in voucher.Vessels)
                vessels.Add(id.Id);
        }
        return vessels;
    }

    /// <summary>The shipyard a faction's ships are sold from.</summary>
    private static ShipyardConsoleUiKey? FactionGroup(WFCrewFaction faction) => faction switch
    {
        WFCrewFaction.TSF => ShipyardConsoleUiKey.Security,
        WFCrewFaction.PDV => ShipyardConsoleUiKey.BlackMarket,
        WFCrewFaction.VG => ShipyardConsoleUiKey.Viper,
        WFCrewFaction.USSP => ShipyardConsoleUiKey.Ussp,
        WFCrewFaction.Underground => ShipyardConsoleUiKey.Syndicate,
        _ => null,
    };

    private bool IsFactionVessel(VesselPrototype vessel, WFCrewFaction faction, HashSet<string>? catalog) =>
        !vessel.Abstract && vessel.Price > 0 && FactionGroup(faction) is { } group && vessel.Group == group &&
        (catalog == null || catalog.Contains(vessel.ID));
}
