using System.Numerics;
using Content.Shared.Maps;
using Content.Shared.Mining;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// One hidden deep vein, spawned by the ground biome's flat marker layer and stamped deterministically at MapInit.
/// The sprite starts invisible and is flipped on client-side per player from <see cref="WFSurveyedComponent"/>, so the
/// vein is networked to everyone in range but drawn and examinable only for players who have pulsed it.
/// </summary>
/// <remarks>
/// Plain AutoGenerateComponentState with no argument is correct here: nothing subscribes AfterAutoHandleStateEvent on
/// this component - the client reacts to ComponentStartup instead - and the analyzer only errors when such a
/// subscription exists.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFDeepVeinComponent : Component
{
    /// <summary>The ore this vein rolled, picked by weight from the world's vein table.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<OrePrototype> Ore;

    /// <summary>Total units in the vein when it was stamped, before any extraction.</summary>
    [DataField, AutoNetworkedField]
    public int TotalYield;

    /// <summary>True in the top quarter of <see cref="YieldRange"/>; drives the richer sprite state.</summary>
    [DataField, AutoNetworkedField]
    public bool Rich;

    /// <summary>
    /// The range <see cref="TotalYield"/> was actually rolled inside, the world's unsanctioned multiplier included.
    /// Stamped alongside the yield because the vein carries no reference back to the table it came from, and both the
    /// rich flag and the examine band have to be read against the range this vein could really have rolled - banding a
    /// doubled unsanctioned yield against the sanctioned ceiling puts every vein in the top bucket.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 YieldRange = WFVeinTablePrototype.DefaultYieldRange;

    /// <summary>Ore units per minute this vein gives up, copied from the table. Read by F6's crack miner.</summary>
    [DataField]
    public float Rate = 150f;

    /// <summary>Units left to extract. F6's; unused by F2.</summary>
    [DataField]
    public int Remaining;

    /// <summary>
    /// Tiles this vein is allowed to sit on; it deletes itself at MapInit on anything else.
    /// This is the only tile filter available: BiomeMarkerLayerPrototype has exactly eight fields in this fork
    /// (Content.Shared/Parallax/Biomes/Markers/BiomeMarkerLayerPrototype.cs:12-52) and none of them is a whitelist -
    /// tile whitelists live on biome LAYERS (IBiomeWorldLayer.AllowedTiles), which marker layers never consult.
    /// The two defaults are Grasslands' fill tiles (Resources/Prototypes/Procedural/biome_templates.yml:124-130) and
    /// deliberately exclude MonoOcean and Snow, the other two templates WFBiomeAsclepiu stacks.
    /// </summary>
    [DataField]
    public List<ProtoId<ContentTileDefinition>> AllowedTiles = new() { "FloorPlanetGrass", "FloorPlanetDirt" };
}
