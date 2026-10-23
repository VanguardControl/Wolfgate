using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// A chop-shop drydock console: it only breaks hulls, hands over a share of the yield as sheets and buys the rest for
/// cash, and posts a warrant on whoever breaks a ship that is neither theirs nor an outlaw's.
/// </summary>
[RegisterComponent]
public sealed partial class WFDrydockChopShopComponent : Component
{
    /// <summary>Share of each recovered material handed over as sheets at the console; the rest is bought for cash.</summary>
    [DataField]
    public float MaterialShare = 0.25f;

    /// <summary>Share of the market price paid for the materials bought.</summary>
    [DataField]
    public float CashRate = 0.75f;

    /// <summary>Flat cash paid per hull tile lifted, on top of the materials bought.</summary>
    [DataField]
    public int TileCash;

    /// <summary>Reward on the warrant posted on whoever breaks lawful property; 0 posts none.</summary>
    [DataField]
    public int WarrantReward = 15000;

    /// <summary>NPC factions whose encounter ships anyone may break.</summary>
    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> OutlawFactions = new();
}
