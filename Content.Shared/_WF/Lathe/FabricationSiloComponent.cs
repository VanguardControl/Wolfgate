using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Lathe;

/// <summary>
/// Stores lathe recipe parts or reagents for linked lathes.
/// </summary>
[RegisterComponent]
public sealed partial class FabricationSiloComponent : Component
{
    /// <summary>
    /// Container that holds a parts silo's stock.
    /// </summary>
    public const string PartsContainerId = "fabrication_parts";

    /// <summary>
    /// Which kind of supply this silo holds.
    /// </summary>
    [DataField]
    public FabricationSiloKind Kind;

    /// <summary>
    /// Furthest a linked machine can be.
    /// </summary>
    [DataField]
    public float Range = 125f;

    /// <summary>
    /// Most entities a parts silo holds.
    /// </summary>
    [DataField]
    public int MaxParts = 250;

    /// <summary>
    /// Machines linked to this silo.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> Clients = new();

    /// <summary>
    /// Stored reagents, kept as separate amounts so they never react.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Reagents = new();

    /// <summary>
    /// Stored parts; only a parts silo has one.
    /// </summary>
    [ViewVariables]
    public Container? Parts;
}

/// <summary>
/// The supply a fabrication silo holds.
/// </summary>
[Serializable, NetSerializable]
public enum FabricationSiloKind : byte
{
    Parts,
    Chemicals,
}
