using Robust.Shared.Prototypes;

namespace Content.Shared._WG.MentorCritters;

/// <summary>One animal a mentor may join an asker as (D16): the entity to spawn and the animal's name.</summary>
[Prototype("wgMentorCritter")]
public sealed partial class WGMentorCritterPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The critter body, a child of <c>WGMentorCritterBase</c>.</summary>
    [DataField(required: true)]
    public EntProtoId Entity;

    /// <summary>Fluent key of the animal's name, as in "Mentor mouse".</summary>
    [DataField(required: true)]
    public LocId Animal;

    /// <summary>Order in the picker.</summary>
    [DataField]
    public int Priority;
}
