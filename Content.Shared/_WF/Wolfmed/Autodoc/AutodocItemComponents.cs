using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>A program disk. In the pod's disk slot it adds its program to the pod's library.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AutodocProgramDiskComponent : Component
{
    [DataField(required: true)]
    public ProtoId<AutodocProgramPrototype> Program;
}

/// <summary>
/// The defibrillator module seam. Nothing reads it beyond the pod's own status line: revival belongs to the
/// BRAIN package, so the module is recognised, shown and otherwise inert.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AutodocDefibModuleComponent : Component;
