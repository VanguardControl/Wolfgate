namespace Content.Server._WG.ColdStorage;

/// <summary>
/// Which wires of a board were cut when its grid was stored, and its seed and serial number. None of a board's wire
/// state is saved, so a load would mend every wire; the layout itself is the round's and comes back by itself.
/// </summary>
[RegisterComponent]
public sealed partial class WGKeptWiresComponent : Component
{
    /// <summary>Ids of the cut wires within the board's layout.</summary>
    [DataField]
    public List<int> Cut = new();

    [DataField]
    public int Seed;

    [DataField]
    public string? Serial;
}
