using Content.Shared.Actions;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Chimera;

public sealed partial class WFPlantFleshPustuleActionEvent : WorldTargetActionEvent
{
    [DataField]
    public EntProtoId Entity = "WFFleshPustule";
}

[Serializable, NetSerializable]
public enum WFFleshPustuleVisuals : byte
{
    Bursted,
}

[Serializable, NetSerializable]
public enum WFFleshPustuleVisualLayers : byte
{
    Closed,
    Popped,
}
