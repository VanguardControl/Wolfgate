using System.Numerics;
using Content.Shared._WF.Caverns;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Caverns;

/// <summary>Links a planet's ground map to the cavern below it, and records every way down.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFCavernGroundComponent : Component
{
    /// <summary>The cavern map below this ground.</summary>
    [ViewVariables]
    public EntityUid Cavern;

    /// <summary>The cavern prototype it was built from.</summary>
    [ViewVariables]
    public ProtoId<WFCavernPrototype> Prototype;

    /// <summary>The planet centre on the ground grid; the gate cell tries centre + gate offset first.</summary>
    [ViewVariables]
    public Vector2 Centre;

    /// <summary>Every mouth cell looked at so far, by cell index.</summary>
    [ViewVariables]
    public Dictionary<Vector2i, WFCavernCell> Cells = new();

    /// <summary>Every mouth stamped on this ground, in the order they were cut.</summary>
    [ViewVariables]
    public List<WFCavernMouth> Mouths = new();

    /// <summary>The shade over each hole tile, by ground tile index.</summary>
    [ViewVariables]
    public Dictionary<Vector2i, EntityUid> Shades = new();

    /// <summary>The climb point under each lip, by tile index (the same on both maps).</summary>
    [ViewVariables]
    public Dictionary<Vector2i, EntityUid> ClimbPoints = new();
}

/// <summary>What became of a mouth cell: claimed, waiting, or without a site.</summary>
public enum WFCavernClaim : byte
{
    /// <summary>Never looked at.</summary>
    Unclaimed,

    /// <summary>The site is stamped.</summary>
    Claimed,

    /// <summary>A site exists but can't be stamped yet: loaded terrain, a grid or something built is in the way.</summary>
    Deferred,

    /// <summary>No candidate passed the pure checks; this cell never gets a mouth.</summary>
    Empty,
}

/// <summary>Why a mouth exists.</summary>
public enum WFCavernMouthKind : byte
{
    /// <summary>The world's mouth near its centre, claimed at build.</summary>
    Gate,

    /// <summary>A cell's mouth, claimed ahead of the terrain streaming in.</summary>
    Cell,

    /// <summary>A hole opened later in loaded ground.</summary>
    Hole,

    /// <summary>Carved by an admin.</summary>
    Admin,
}

/// <summary>One mouth cell's cached site and claim state.</summary>
public sealed class WFCavernCell
{
    /// <summary>Where the cell stands.</summary>
    [ViewVariables]
    public WFCavernClaim State = WFCavernClaim.Unclaimed;

    /// <summary>Whether the pure checks have run; the site never changes once they have.</summary>
    [ViewVariables]
    public bool Evaluated;

    /// <summary>The hole's bottom-left tile, or null when no candidate passed.</summary>
    [ViewVariables]
    public Vector2i? Site;
}

/// <summary>A stamped way down: a square hole, its lip and the climb tile beside it.</summary>
/// <param name="Origin">The hole's bottom-left ground tile.</param>
/// <param name="Size">The hole's edge in tiles.</param>
/// <param name="ClimbTile">The lip tile over the climb point.</param>
/// <param name="Kind">Why the mouth exists.</param>
public readonly record struct WFCavernMouth(Vector2i Origin, int Size, Vector2i ClimbTile, WFCavernMouthKind Kind)
{
    /// <summary>The hole's centre in ground-local coordinates.</summary>
    public Vector2 Centre => new(Origin.X + Size / 2f, Origin.Y + Size / 2f);

    /// <summary>Whether a ground tile lies inside the hole.</summary>
    public bool Contains(Vector2i tile)
    {
        return tile.X >= Origin.X && tile.X < Origin.X + Size && tile.Y >= Origin.Y && tile.Y < Origin.Y + Size;
    }
}
