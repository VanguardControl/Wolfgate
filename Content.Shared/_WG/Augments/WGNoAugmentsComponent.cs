namespace Content.Shared._WG.Augments;

/// <summary>On a species' mob prototype: no augment slot is offered in the creator and autosurgeons refuse the body.</summary>
[RegisterComponent]
public sealed partial class WGNoAugmentsComponent : Component
{
    /// <summary>Shown in the creator's Augments tab in place of the part hint.</summary>
    [DataField(required: true)]
    public LocId Reason;

    /// <summary>Popup when an autosurgeon is used on the body; takes $target.</summary>
    [DataField(required: true)]
    public LocId AutosurgeonRefuse;
}
