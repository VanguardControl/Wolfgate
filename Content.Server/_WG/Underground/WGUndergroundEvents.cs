namespace Content.Server._WG.Underground;

/// <summary>Raised when the Underground's Heat level changes.</summary>
public sealed class WGUndergroundHeatChangedEvent(int old, int level) : EntityEventArgs
{
    public readonly int Old = old;
    public readonly int Level = level;
}

/// <summary>Raised on someone about to be given their delayed components, such as a heretic mark, so it can be refused.</summary>
[ByRefEvent]
public record struct WGMarkAttemptEvent(bool Cancelled = false);
