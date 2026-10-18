namespace Content.Server._WF.MappingMode;

/// <summary>
/// The per-player map library: versioned saves, autosaves and downloads.
/// </summary>
public sealed class WFMappingLibrarySystem : EntitySystem
{
    /// <summary>
    /// Saves every dirty session into its owner's library, before a round restart deletes the maps.
    /// </summary>
    public void AutosaveAll()
    {
        // S2
    }

    /// <summary>
    /// Saves one session into its owner's library as an autosave; true when it was written.
    /// </summary>
    public bool Autosave(WFMappingSession session)
    {
        // S2
        return false;
    }
}
