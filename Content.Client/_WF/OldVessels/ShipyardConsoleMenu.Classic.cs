using System.Linq;
using Content.Client._WF.OldVessels;
using Content.Shared._NF.Shipyard.Prototypes;

namespace Content.Client._NF.Shipyard.UI;

/// <summary>
/// The shipyard menu's classic ships filter (all, current only, classic only) and the Classic marker on its rows.
/// </summary>
public sealed partial class ShipyardConsoleMenu
{
    private enum ClassicFilterMode : byte
    {
        All,
        Current,
        Classic,
    }

    private ClassicFilterMode _classicFilter = ClassicFilterMode.All;

    private void InitClassicFilter()
    {
        ClassicFilter.AddItem(Loc.GetString("wf-shipyard-classic-filter-all"), (int) ClassicFilterMode.All);
        ClassicFilter.AddItem(Loc.GetString("wf-shipyard-classic-filter-current"), (int) ClassicFilterMode.Current);
        ClassicFilter.AddItem(Loc.GetString("wf-shipyard-classic-filter-classic"), (int) ClassicFilterMode.Classic);
        ClassicFilter.Visible = false;
        ClassicFilter.OnItemSelected += args =>
        {
            ClassicFilter.SelectId(args.Id);
            _classicFilter = (ClassicFilterMode) args.Id;
            PopulateProducts(_lastAvailableProtos, _lastUnavailableProtos, _freeListings, _validId);
        };
    }

    /// <summary>
    /// Shows the filter only when the listing has a classic ship; otherwise it resets to all ships.
    /// </summary>
    public void PopulateClassicFilter(List<string> availablePrototypes, List<string> unavailablePrototypes)
    {
        var anyClassic = availablePrototypes.Concat(unavailablePrototypes)
            .Any(id => _protoManager.TryIndex<VesselPrototype>(id, out var vessel) && ClassicVessels.IsClassic(vessel));
        ClassicFilter.Visible = anyClassic;

        if (!anyClassic && _classicFilter != ClassicFilterMode.All)
        {
            _classicFilter = ClassicFilterMode.All;
            PopulateProducts(_lastAvailableProtos, _lastUnavailableProtos, _freeListings, _validId);
        }

        ClassicFilter.SelectId((int) _classicFilter);
    }

    private bool PassesClassicFilter(VesselPrototype vessel)
    {
        return _classicFilter switch
        {
            ClassicFilterMode.Current => !ClassicVessels.IsClassic(vessel),
            ClassicFilterMode.Classic => ClassicVessels.IsClassic(vessel),
            _ => true,
        };
    }

    private static void MarkClassic(VesselRow row)
    {
        row.ClassicLabel.Visible = row.Vessel != null && ClassicVessels.IsClassic(row.Vessel);
    }
}
