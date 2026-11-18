using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.AdminTech;

/// <summary>Clothing that gives its wearer tags while worn in its own slot, and takes back the ones it gave.</summary>
[RegisterComponent]
public sealed partial class WGGrantTagsComponent : Component
{
    [DataField(required: true)]
    public List<ProtoId<TagPrototype>> Tags = new();

    /// <summary>The tags the current wearer did not have before, so only those are removed.</summary>
    [ViewVariables]
    public List<ProtoId<TagPrototype>> Given = new();
}
