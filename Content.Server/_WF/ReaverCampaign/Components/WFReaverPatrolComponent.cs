using System.Numerics;

namespace Content.Server._WF.ReaverCampaign.Components;

/// <summary>A Reaver patrol, on its encounter entity: the stronghold that sent it and the route it flies.</summary>
[RegisterComponent]
public sealed partial class WFReaverPatrolComponent : Component
{
    [ViewVariables]
    public EntityUid Stronghold;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    /// <summary>The cell centres it calls at, then home.</summary>
    [ViewVariables]
    public List<Vector2> Route = new();

    [ViewVariables]
    public Vector2 Home;

    /// <summary>Where its lead was last seen, for the cell it died in.</summary>
    [ViewVariables]
    public Vector2 LastSeen;

    /// <summary>Whether it was called back to defend its stronghold.</summary>
    [ViewVariables]
    public bool Recalled;
}
