using Content.Server._WG.Underground;

namespace Content.Server._NF.Contraband.Systems;

/// <summary>Lets the Underground module decide which prisoners and bodies a contraband pallet takes, and what for.</summary>
public sealed partial class ContrabandTurnInSystem
{
    [Dependency] private WGUndergroundWarrantSystem _wgWarrants = default!;

    /// <summary>The currency of the pallet console being appraised or sold from.</summary>
    private string _wgRewardType = string.Empty;

    private bool WgCanTurnIn(EntityUid mob)
    {
        return _wgWarrants.CanTurnIn(mob, _wgRewardType);
    }

    private int WgTurnInValue(EntityUid ent, int value)
    {
        return _wgWarrants.TurnInValue(ent, _wgRewardType, value);
    }

    /// <summary>Records the sale's prisoners and takes the bodies the pallet keeps out of the set to delete.</summary>
    private void WgTurnedIn(HashSet<EntityUid> sold)
    {
        _wgWarrants.TurnedIn(sold, _wgRewardType);
    }
}
