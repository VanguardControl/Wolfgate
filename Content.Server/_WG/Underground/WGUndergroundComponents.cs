using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Underground;

/// <summary>Someone who works for the Underground, with the warrant the TSF pays for them alive.</summary>
[RegisterComponent]
public sealed partial class WGUndergroundMemberComponent : Component
{
    /// <summary>Military credits the warrant starts at, and returns to after a capture.</summary>
    [DataField]
    public int BaseWarrant = 25;

    /// <summary>The most the warrant grows to.</summary>
    [DataField]
    public int MaxWarrant = 150;

    /// <summary>What the warrant pays now.</summary>
    [ViewVariables]
    public int Warrant;

    /// <summary>Until when no pallet takes them, after a capture or bail.</summary>
    [ViewVariables]
    public TimeSpan LockedUntil;

    /// <summary>The player who spawned in this body, kept for when they have left it.</summary>
    [ViewVariables]
    public NetUserId? User;
}

/// <summary>A captured member a pallet is holding while the Underground can pay bail.</summary>
[RegisterComponent]
public sealed partial class WGUndergroundHeldComponent : Component
{
    /// <summary>When the pallet will take them if bail isn't paid.</summary>
    [ViewVariables]
    public TimeSpan Until;

    /// <summary>Thalers of bail.</summary>
    [ViewVariables]
    public int Bail;
}

/// <summary>A point-defence turret of the Underground station, armed in a raid or by the Godfather.</summary>
[RegisterComponent]
public sealed partial class WGUndergroundTurretComponent : Component
{
}

/// <summary>A console the Underground runs its faction business from, through its verbs.</summary>
[RegisterComponent]
public sealed partial class WGUndergroundTerminalComponent : Component
{
    /// <summary>The pass it hands out.</summary>
    [DataField]
    public EntProtoId Pass = "WGUndergroundMarketPass";

    /// <summary>When it can issue the next pass.</summary>
    [ViewVariables]
    public TimeSpan NextPass;
}

/// <summary>The Underground's station grid: where its safehouse and extra job slots are kept track of.</summary>
[RegisterComponent]
public sealed partial class WGUndergroundBaseComponent : Component
{
    /// <summary>The ship parked away from the station as a second place to wake up.</summary>
    [DataField]
    public ProtoId<VesselPrototype> SafehouseVessel = "WGUBMLurker";

    /// <summary>Spawn markers put on the safehouse's seats.</summary>
    [DataField]
    public List<EntProtoId> SafehouseSpawns = new() { "SpawnPointFugitive", "WGSpawnPointUndergroundFixer" };

    /// <summary>The most seats that get spawn markers.</summary>
    [DataField]
    public int SafehouseSeats = 4;

    /// <summary>Whether the safehouse has been tried this round.</summary>
    [ViewVariables]
    public bool SafehouseTried;

    /// <summary>The safehouse ship, once parked.</summary>
    [ViewVariables]
    public EntityUid? Safehouse;

    /// <summary>Fugitive slots the population has added so far.</summary>
    [ViewVariables]
    public int BonusSlots;
}
