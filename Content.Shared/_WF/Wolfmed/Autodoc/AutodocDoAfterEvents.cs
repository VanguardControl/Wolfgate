using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>Forcing the lid with a prying tool while the pod has it locked.</summary>
[Serializable, NetSerializable]
public sealed partial class AutodocPryDoAfterEvent : SimpleDoAfterEvent;
