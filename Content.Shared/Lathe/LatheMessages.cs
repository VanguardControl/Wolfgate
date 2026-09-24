using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Lathe;

[Serializable, NetSerializable]
public sealed class LatheUpdateState : BoundUserInterfaceState
{
    public List<ProtoId<LatheRecipePrototype>> Recipes;

    public List<LatheRecipeBatch> Queue; // Frontier: LatheRecipePrototype<LatheRecipeBatch

    public LatheRecipePrototype? CurrentlyProducing;

    public bool Looping = false; // Mono
    public bool Skipping = false; // Mono
    public bool PartsSiloLinked;
    public bool ChemicalSiloLinked;
    // These lists correspond to Recipes and Queue respectively. Each value
    // describes whether the next single item can start with current supplies.
    public List<bool> RecipeReady;
    public List<bool> QueueReady;
    public List<LatheMissingSupplies> QueueMissingSupplies;

    public LatheUpdateState(List<ProtoId<LatheRecipePrototype>> recipes,
        List<LatheRecipeBatch> queue,
        LatheRecipePrototype? currentlyProducing = null,
        bool looping = false,
        bool skipping = false,
        bool partsSiloLinked = false,
        bool chemicalSiloLinked = false,
        List<bool>? recipeReady = null,
        List<bool>? queueReady = null,
        List<LatheMissingSupplies>? queueMissingSupplies = null) // Frontier: change queue type // Mono
    {
        Recipes = recipes;
        Queue = queue;
        CurrentlyProducing = currentlyProducing;
        Looping = looping; // Mono
        Skipping = skipping; // Mono
        PartsSiloLinked = partsSiloLinked;
        ChemicalSiloLinked = chemicalSiloLinked;
        RecipeReady = recipeReady ?? new List<bool>();
        QueueReady = queueReady ?? new List<bool>();
        QueueMissingSupplies = queueMissingSupplies ?? new List<LatheMissingSupplies>();
    }
}

/// <summary>
/// Supplies missing for the next item in one queued batch, including linked silos.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheMissingSupplies
{
    public bool DesignAvailable = true;
    public Dictionary<ProtoId<MaterialPrototype>, int> Materials = new();
    public Dictionary<EntProtoId, int> Entities = new();
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Reagents = new();
}

/// <summary>
///     Sent to the server to sync material storage and the recipe queue.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheSyncRequestMessage : BoundUserInterfaceMessage
{

}

/// <summary>
///     Sent to the server when a client queues a new recipe.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheQueueRecipeMessage : BoundUserInterfaceMessage
{
    public readonly string ID;
    public readonly int Quantity;
    public LatheQueueRecipeMessage(string id, int quantity)
    {
        ID = id;
        Quantity = quantity;
    }
}

// Mono
/// <summary>
///     Sent to the server when a client wants to change whether the lathe should loop.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheSetLoopingMessage : BoundUserInterfaceMessage
{
    public readonly bool ShouldLoop;
    public LatheSetLoopingMessage(bool shouldLoop)
    {
        ShouldLoop = shouldLoop;
    }
}

// Mono
/// <summary>
///     Sent to the server when a client wants to change whether the lathe should skip over unavailable recipes.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheSetSkipMessage : BoundUserInterfaceMessage
{
    public readonly bool ShouldSkip;
    public LatheSetSkipMessage(bool shouldSkip)
    {
        ShouldSkip = shouldSkip;
    }
}

// Mono
/// <summary>
///     Sent to the server when a client wants to de-queue a recipe from the lathe.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheRecipeCancelMessage : BoundUserInterfaceMessage
{
    public readonly int Index;
    public LatheRecipeCancelMessage(int index)
    {
        Index = index;
    }
}

/// <summary>
///     Changes the requested total for an existing queued batch.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheRecipeAmountMessage : BoundUserInterfaceMessage
{
    public readonly int Index;
    public readonly int Amount;

    public LatheRecipeAmountMessage(int index, int amount)
    {
        Index = index;
        Amount = amount;
    }
}

[NetSerializable, Serializable]
public enum LatheUiKey
{
    Key,
}
