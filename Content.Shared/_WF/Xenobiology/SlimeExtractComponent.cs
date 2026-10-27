using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenobiology;

/// <summary>
/// A slime extract that reacts to reagents put into its solution.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlimeExtractComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<ProtoId<ExtractReactionPrototype>> ExtractReactions = new();

    /// <summary>
    /// Name of the solution the reagents go into.
    /// </summary>
    [DataField(required: true)]
    public string ContainerName = string.Empty;

    /// <summary>
    /// Reactions left before the extract is exhausted.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int RemainingUses = 1;
}

/// <summary>
/// Marks a pre-filled extract so the spawn and delete tests skip it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FilledSlimeExtractComponent : Component;
