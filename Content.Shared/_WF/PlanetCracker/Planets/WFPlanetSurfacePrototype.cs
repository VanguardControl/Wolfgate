using Content.Shared._DV.Planet;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.PlanetCracker.Planets;

/// <summary>
/// Describes the whole z-stack of one sector planet: its ground biome, how many air layers sit above it, and its orbit layer.
/// </summary>
/// <remarks>
/// The prototype kind string is declared explicitly. Robust derives an unqualified kind by lowercasing only index 0,
/// which would register this type as "wFPlanetSurface" and break every "- type: wfPlanetSurface" document.
/// </remarks>
[Prototype("wfPlanetSurface")]
public sealed partial class WFPlanetSurfacePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The sector body type this surface belongs to; the registry matches spawned planets by this id.</summary>
    [DataField(required: true)]
    public ProtoId<PlanetTypePrototype> PlanetType;

    /// <summary>The planet prototype the ground layer is generated from.</summary>
    [DataField(required: true)]
    public ProtoId<PlanetPrototype> Ground;

    /// <summary>Optional hand-made grid loaded onto the ground layer at the planet centre.</summary>
    [DataField]
    public ResPath? GroundGrid;

    /// <summary>How many bare fall-through air layers sit between the ground and the cloud layer.</summary>
    [DataField]
    public int AirLayers = 2;

    /// <summary>Whether a cloud layer sits between the topmost air layer and orbit.</summary>
    [DataField]
    public bool CloudLayer = true;

    /// <summary>
    /// Surface gravity in gees, stamped onto every layer of the stack. A hull's landing-thruster lift is divided by
    /// it, so doubling this halves the lift ratio of everything that tries to fly here.
    /// </summary>
    [DataField]
    public float Gravity = 1f;

    /// <summary>How close to the sector planet a shuttle must be to enter orbit.</summary>
    [DataField]
    public float OrbitRange = 2000f;

    /// <summary>
    /// Speed cap (m/s) for a grid on this world's orbit layer, stamped onto <see cref="WFOrbitLayerComponent"/>.
    /// Orbit is where the surface streams in under a hull, so it is the tightest cap in the stack.
    /// </summary>
    [DataField]
    public float OrbitMaxSpeed = 6f;

    /// <summary>Linear damping a grid on this world's orbit layer flies under; thrusters fight it rather than beat it.</summary>
    [DataField]
    public float OrbitDamping = 3f;

    /// <summary>Speed cap (m/s) for a grid on one of this world's air layers, stamped onto every layer marker.</summary>
    [DataField]
    public float AirMaxSpeed = 12f;

    /// <summary>Linear damping a grid flying one of this world's air layers runs under.</summary>
    [DataField]
    public float AirDamping = 1.5f;

    /// <summary>Whether the network is built eagerly when the sector body spawns at round start.</summary>
    [DataField]
    public bool BuildAtRoundStart;

    /// <summary>Whether cracking this world is legal; F9 announces the unsanctioned case, F2 only stores and displays it.</summary>
    [DataField]
    public bool Sanctioned = true;

    /// <summary>The deep-vein table this world rolls every vein from; null means no deep veins and no rating.</summary>
    [DataField]
    public ProtoId<WFVeinTablePrototype>? Veins;

    /// <summary>The salvage faction fissure mobs are rolled from on a sanctioned world; null means no fissure mobs.</summary>
    [DataField]
    public ProtoId<SalvageFactionPrototype>? Faction;

    /// <summary>The nastier table an unsanctioned crack rolls from; falls back to Faction when unset.</summary>
    [DataField]
    public ProtoId<SalvageFactionPrototype>? UnsanctionedFaction;

    /// <summary>
    /// Fixes the ground biome seed so a planet's terrain AND its deep veins are identical every round; null keeps the
    /// engine's random seed. PlanetSystem.SpawnPlanet passes no seed, so EnsurePlanet rolls _random.Next()
    /// (Content.Server/Parallax/BiomeSystem.PlanetSetup.cs:33) and the world re-rolls on every server start.
    /// </summary>
    [DataField]
    public int? Seed;

    /// <summary>Components stamped on every member map by the z-network registry, transit maps included.</summary>
    [DataField]
    public ComponentRegistry? NetworkComponents;

    /// <summary>Components added to the ground layer after the network is initialised.</summary>
    [DataField]
    public ComponentRegistry? GroundComponents;

    /// <summary>Components added to each air layer after the network is initialised.</summary>
    [DataField]
    public ComponentRegistry? AirComponents;

    /// <summary>Components added to the cloud layer after the network is initialised.</summary>
    [DataField]
    public ComponentRegistry? CloudComponents;

    /// <summary>Components added to the orbit layer after the network is initialised.</summary>
    [DataField]
    public ComponentRegistry? OrbitComponents;

    /// <summary>Name given to the orbit map entity; the console's orbit button and the survey rows read it.</summary>
    [DataField]
    public LocId OrbitMapName = "wf-planet-orbit-map-name";

    /// <summary>Name given to the orbit layer's centre marker; a warp point and radar label, not an FTL beacon.</summary>
    [DataField]
    public LocId OrbitMarkerName = "wf-planet-orbit-marker-name";
}
