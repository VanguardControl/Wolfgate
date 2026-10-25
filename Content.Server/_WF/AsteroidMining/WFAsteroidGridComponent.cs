using Content.Shared._WF.AsteroidMining;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.AsteroidMining;

/// <summary>Builds an asteroid grid on map init: a disc of invisible tiles with one rock anchored at its centre.</summary>
[RegisterComponent]
public sealed partial class WFAsteroidGridComponent : Component
{
    [DataField(required: true)] public EntProtoId Rock;

    [DataField] public string Tile = "WFAsteroidCore";

    /// <summary>Rolls the rock's ore when set; fragments leave it empty and copy their parent's instead.</summary>
    [DataField] public ProtoId<WFAsteroidCompositionPrototype>? Composition;

    public EntityUid? RockUid;
}
