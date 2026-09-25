using System.Numerics;
using Content.Shared.Maps;
using Content.Shared.Mining;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// One hidden deep vein, stamped at MapInit; drawn and examinable only for players who have pulsed it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFDeepVeinComponent : Component
{
    /// <summary>The ore this vein rolled, picked by weight from the world's vein table; null until stamped.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<OrePrototype>? Ore;

    /// <summary>Total units in the vein when it was stamped, before any extraction.</summary>
    [DataField, AutoNetworkedField]
    public int TotalYield;

    /// <summary>True in the top quarter of <see cref="YieldRange"/>; drives the richer sprite state.</summary>
    [DataField, AutoNetworkedField]
    public bool Rich;

    /// <summary>The range <see cref="TotalYield"/> was rolled in, unsanctioned multiplier included; bands are read against it.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 YieldRange = WFVeinTablePrototype.DefaultYieldRange;

    /// <summary>Ore units per minute this vein gives up to a crack miner, copied from the table.</summary>
    [DataField]
    public float Rate = 150f;

    /// <summary>Units left to extract.</summary>
    [DataField]
    public int Remaining;

    /// <summary>Tiles this vein may sit on; it deletes itself at MapInit elsewhere, as marker layers have no tile whitelist.</summary>
    [DataField]
    public List<ProtoId<ContentTileDefinition>> AllowedTiles = new() { "FloorPlanetGrass", "FloorPlanetDirt" };
}
