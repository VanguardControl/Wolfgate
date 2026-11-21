namespace Content.Server._WG.ColdStorage;

/// <summary>On the stand-in left where a stored grid was: what it stands for.</summary>
[RegisterComponent]
public sealed partial class WGStoredGridComponent : Component
{
    /// <summary>The key of the grid's bytes in the cold store.</summary>
    [ViewVariables]
    public string Key = string.Empty;

    /// <summary>The grid's bounds in world space when it was stored.</summary>
    [ViewVariables]
    public Box2 Bounds;

    [ViewVariables]
    public int Entities;

    [ViewVariables]
    public string GridName = string.Empty;
}
