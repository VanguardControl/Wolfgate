using System.Diagnostics.CodeAnalysis;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes.Markers;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker;

/// <summary>What cracking needs to know about one world: its vein table, fissure factions and deep-vein marker layers.</summary>
// Kind named explicitly: Robust would derive "wFCrackSite".
[Prototype("wfCrackSite")]
public sealed partial class WFCrackSitePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The planet surface this site describes; at most one site per surface.</summary>
    [DataField(required: true)]
    public ProtoId<WFPlanetSurfacePrototype> Surface;

    /// <summary>The deep-vein table this world rolls every vein from; null means no deep veins and no rating.</summary>
    [DataField]
    public ProtoId<WFVeinTablePrototype>? Veins;

    /// <summary>The salvage faction fissure mobs are rolled from on a sanctioned world; null means no fissure mobs.</summary>
    [DataField]
    public ProtoId<SalvageFactionPrototype>? Faction;

    /// <summary>The nastier table an unsanctioned crack rolls from; falls back to Faction when unset.</summary>
    [DataField]
    public ProtoId<SalvageFactionPrototype>? UnsanctionedFaction;

    /// <summary>Marker layers added to the ground after the planet's own, so deep veins are placed after its ores.</summary>
    [DataField]
    public List<ProtoId<BiomeMarkerLayerPrototype>> MarkerLayers = new();

    /// <summary>Finds the crack site that describes a surface; false when the world has none.</summary>
    public static bool TryGet(IPrototypeManager proto,
        ProtoId<WFPlanetSurfacePrototype> surface,
        [NotNullWhen(true)] out WFCrackSitePrototype? site)
    {
        foreach (var candidate in proto.EnumeratePrototypes<WFCrackSitePrototype>())
        {
            if (candidate.Surface != surface)
                continue;

            site = candidate;
            return true;
        }

        site = null;
        return false;
    }
}
