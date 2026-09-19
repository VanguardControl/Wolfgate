using System.Numerics;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.PlanetCracker.Fissures;

/// <summary>
/// Sits on a gravity anchor and spreads rings of fissure decals and site threats while its drill runs.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WFFissureSpawnerComponent : Component
{
    /// <summary>
    /// When the next ring is due. The cadence is WFGravityAnchorComponent.DrillDuration / <see cref="RingCount"/>,
    /// read live off the anchor, and this field is seeded to CurTime at arm so ring 1 fires immediately and the last
    /// of five rings is due at 80% of the drill instead of racing the anchor's own 1 Hz lock sweep
    /// (WFGravityAnchorSystem.cs:292-302). DrillEnd is NOT a usable drill-start source: WFGravityAnchorSystem.Control.cs:20
    /// rewrites it to CurTime on completion and WFGravityAnchorSystem.Pairing.cs:108 zeroes it on demote.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextRing;

    /// <summary>Whether a drill is running and rings are being spread.</summary>
    [ViewVariables]
    public bool Armed;

    /// <summary>How many rings this anchor has already spread.</summary>
    [ViewVariables]
    public int RingsDone;

    /// <summary>How many rings one full drill spreads.</summary>
    [DataField]
    public int RingCount = 5;

    /// <summary>Radius of the first ring in tiles; the anchor's own 3x3 footprint is already reserved.</summary>
    [DataField]
    public float FirstRingRadius = 2f;

    /// <summary>Tiles added to the radius for each subsequent ring.</summary>
    [DataField]
    public float RingStep = 1.5f;

    /// <summary>Fewest fissures one ring stamps.</summary>
    [DataField]
    public int MinFissures = 2;

    /// <summary>Most fissures one ring stamps.</summary>
    [DataField]
    public int MaxFissures = 4;

    /// <summary>Fewest mobs one ring spawns.</summary>
    [DataField]
    public int MinMobs = 2;

    /// <summary>Most mobs one ring spawns.</summary>
    [DataField]
    public int MaxMobs = 4;

    /// <summary>Multiplier applied to both the fissure and the mob count when the world is unsanctioned.</summary>
    [DataField]
    public float UnsanctionedMultiplier = 1.5f;

    /// <summary>
    /// Most mobs this anchor may ever spawn. <see cref="SpawnedTotal"/> is CUMULATIVE and is never decremented on
    /// death: a live count would let players re-open the budget by killing what is already out, which would make a
    /// five minute drill unbounded.
    /// </summary>
    [DataField]
    public int Cap = 15;

    /// <summary>How many mobs this anchor has spawned in total; only ever counts up, and only on an actual spawn.</summary>
    [ViewVariables]
    public int SpawnedTotal;

    /// <summary>How many threats the extraction surge spawns.</summary>
    [DataField]
    public int SurgeMobs = 6;

    /// <summary>How many threats the extraction surge has already spawned.</summary>
    [ViewVariables]
    public int SurgeSpawned;

    /// <summary>
    /// How many times a granted mob slot may re-roll its group before the slot is abandoned.
    /// EntitySpawnCollection.GetSpawns can legitimately return an EMPTY list:
    /// Resources/Prototypes/Procedural/salvage_factions.yml:17-21 is a Xenos group whose only entry is
    /// `NFMobXenoDrone amount: 0 maxAmount: 2`, and EntitySpawnEntry.GetAmount
    /// (Content.Shared/Storage/EntitySpawnEntry.cs:252-265) returns random.Next(0, 2), so it comes back empty about
    /// half the time that group is drawn. A granted slot must re-roll rather than be silently lost.
    /// </summary>
    [DataField]
    public int MobRollRetries = 5;

    /// <summary>
    /// The planet ground layer this anchor is drilling into, resolved at arm.
    /// Runtime only, never a DataField: a raw EntityUid does not survive a map save and reload, and the same is true of
    /// every other live field below (the WFPlanetChunkComponent.RimDecals/DropStream precedent).
    /// </summary>
    [ViewVariables]
    public EntityUid? Ground;

    /// <summary>The anchor's world position, cached at arm, that the rings are built around.</summary>
    [ViewVariables]
    public Vector2 Centre;

    /// <summary>The salvage faction mobs are rolled from, resolved from the world's surface prototype; null spawns nothing.</summary>
    [DataField]
    public ProtoId<SalvageFactionPrototype>? Faction;

    /// <summary>Whether cracking this world is legal; drives the unsanctioned multiplier and the faction choice.</summary>
    [DataField]
    public bool Sanctioned = true;

    /// <summary>
    /// Every fissure decal this anchor has stamped. Parallel to <see cref="DecalStages"/> (same index is the same
    /// decal) so the ring promoter can raise a decal one growth stage instead of stamping a second decal on the tile.
    /// </summary>
    [ViewVariables]
    public List<uint> Decals = new();

    /// <summary>Growth stage 1-4 of each entry in <see cref="Decals"/>, at the same index.</summary>
    [ViewVariables]
    public List<byte> DecalStages = new();

    /// <summary>Every tile index this anchor has opened a fissure on.</summary>
    [ViewVariables]
    public List<Vector2i> Fissures = new();

    /// <summary>
    /// Every entity this anchor's fissures put on the ground, stamped or not. <see cref="Live"/> is the stamped
    /// subset, so anything that is not a mob - WeaponTurretXeno, salvage_factions.yml:22-25 - is tracked only here.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Spawned = new();

    /// <summary>
    /// The threats this anchor has spawned and STAMPED as site threats. Only a mob joins it: an entry with no
    /// MobStateComponent is left on its own root task and stays in <see cref="Spawned"/> alone.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Live = new();

    /// <summary>
    /// The one-shot effect played on a tile as its fissure opens. The stock spark burst, which is already the
    /// half-second one-shot the rest of the tree spawns for exactly this; no fissure effect art of our own exists.
    /// </summary>
    [DataField]
    public EntProtoId BurstEffect = "EffectSparks";

    /// <summary>Played once per ring as the ground splits.</summary>
    [DataField]
    public SoundSpecifier CrackSound = new SoundPathSpecifier("/Audio/Effects/break_stone.ogg");

    /// <summary>Played on each mob as it emerges.</summary>
    [DataField]
    public SoundSpecifier EmergeSound = new SoundPathSpecifier("/Audio/Effects/stonedoor_openclose.ogg");
}
