namespace Content.Shared._WF.Performance;

/// <summary>Helpers for reused scratch collections.</summary>
public static class WFCollectionExtensions
{
    /// <summary>
    /// Clears a reused set and releases its storage if it was large. A hash set's clear wipes its whole capacity, so
    /// one crowded query would otherwise slow every later clear for good.
    /// </summary>
    public static void ClearAndTrim<T>(this HashSet<T> set, int trimAbove = 256)
    {
        var large = set.Count > trimAbove;
        set.Clear();
        if (large)
            set.TrimExcess();
    }
}
