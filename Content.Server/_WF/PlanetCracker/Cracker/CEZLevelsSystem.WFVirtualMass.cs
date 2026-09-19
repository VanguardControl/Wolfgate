using Content.Shared._WF.PlanetCracker.Cracker;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>Mass content adds to a grid's pooled gravgen load: anchors and crates riding as cargo (design D11).</summary>
    /// <param name="grid">Grid to weigh when no network is supplied.</param>
    /// <param name="networkGrids">Every member of the grid's z-network, when the caller pooled one.</param>
    public float GetWFVirtualMass(EntityUid grid, IReadOnlyCollection<EntityUid>? networkGrids = null)
    {
        if (networkGrids == null)
            return TryComp<WFGridAnchorLoadComponent>(grid, out var load) ? load.VirtualMass : 0f;

        var mass = 0f;

        foreach (var member in networkGrids)
        {
            if (TryComp<WFGridAnchorLoadComponent>(member, out var memberLoad))
                mass += memberLoad.VirtualMass;
        }

        return mass;
    }
}
