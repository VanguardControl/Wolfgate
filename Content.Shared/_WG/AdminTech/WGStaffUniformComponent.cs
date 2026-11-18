namespace Content.Shared._WG.AdminTech;

/// <summary>Clothing that marks its wearer as staff while worn, the way a manifested admin observer is marked.</summary>
[RegisterComponent]
public sealed partial class WGStaffUniformComponent : Component
{
    /// <summary>Whether this clothing put the marker on its wearer, so taking it off removes it.</summary>
    [ViewVariables]
    public bool Marked;
}
