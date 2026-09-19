using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical;

[Serializable, NetSerializable]
public sealed partial class HealingDoAfterEvent : SimpleDoAfterEvent
{
    /// <summary>The wound-host limb selected when treatment started, retained across repeats.</summary>
    public NetEntity? RequestedPart;

    public override DoAfterEvent Clone() => new HealingDoAfterEvent { RequestedPart = RequestedPart };
}
