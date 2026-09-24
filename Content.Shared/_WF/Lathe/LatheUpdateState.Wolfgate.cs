using Content.Shared._WF.Lathe;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Lathe;

public sealed partial class LatheUpdateState
{
    /// <summary>
    /// A parts silo is linked and can supply the lathe.
    /// </summary>
    public bool PartsSiloLinked;

    /// <summary>
    /// A chemical silo is linked and can supply the lathe.
    /// </summary>
    public bool ChemicalSiloLinked;

    /// <summary>
    /// Recipes whose next item can start now.
    /// </summary>
    public HashSet<ProtoId<LatheRecipePrototype>> ReadyRecipes = new();

    /// <summary>
    /// What each queued batch still needs, by batch index.
    /// </summary>
    public Dictionary<int, LatheMissingSupplies> QueueSupplies = new();

    /// <summary>
    /// Index of the batch the item being printed came from.
    /// </summary>
    public int? PrintingBatch;

    /// <summary>
    /// Part counts in the linked parts silo.
    /// </summary>
    public Dictionary<EntProtoId, int> SiloParts = new();

    /// <summary>
    /// Reagent amounts in the linked chemical silo.
    /// </summary>
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> SiloReagents = new();
}
