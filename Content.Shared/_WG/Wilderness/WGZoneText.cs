namespace Content.Shared._WG.Wilderness;

/// <summary>A zone's title and summary as shown, given the war level, who may fight and who staying marks.</summary>
public static class WGZoneText
{
    public static string Title(WGZonePrototype zone, bool hot)
    {
        return Loc.GetString(hot && zone.HotTitle is { } title ? title : zone.Title);
    }

    public static string Summary(WGZonePrototype zone, bool hot, bool openToAll, bool onFoot)
    {
        var key = hot && zone.HotSummary is { } summary ? summary : zone.Summary;
        return Loc.GetString(key, ("open", openToAll ? "yes" : "no"), ("onfoot", onFoot ? "yes" : "no"));
    }

    /// <summary>"9:42" for a span, never negative.</summary>
    public static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        return $"{(int) span.TotalMinutes}:{span.Seconds:00}";
    }
}
