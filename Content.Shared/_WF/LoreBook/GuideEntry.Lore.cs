namespace Content.Shared.Guidebook;

public partial class GuideEntry
{
    /// <summary>
    /// This entry is a chapter of the lore book, not the guidebook. Its children belong to the lore book too.
    /// </summary>
    [DataField]
    public bool Lore;
}
