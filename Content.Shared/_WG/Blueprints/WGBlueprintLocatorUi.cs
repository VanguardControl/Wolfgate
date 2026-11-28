using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Blueprints;

/// <summary>The R&D console's window: the Codex tree, relic patterns and the hunt.</summary>
[Serializable, NetSerializable]
public enum WGBlueprintLocatorUiKey : byte
{
    Key,
}

/// <summary>One pattern the console can hunt; Ready means its technology's prerequisites are known.</summary>
[Serializable, NetSerializable]
public sealed record WGBlueprintPatternState(string Id, bool Owned, bool Ready);

/// <summary>The console's running hunt.</summary>
[Serializable, NetSerializable]
public sealed record WGBlueprintHuntState(string Site, string World, int Tier, List<string> Patterns, int Remaining);

[Serializable, NetSerializable]
public sealed class WGBlueprintLocatorState : BoundUserInterfaceState
{
    public readonly bool Linked;
    public readonly string? ServerName;
    public readonly List<WGBlueprintPatternState> Patterns;
    public readonly int[] Fees;
    public readonly int MaxPatterns;
    public readonly WGBlueprintHuntState? Hunt;
    public readonly TimeSpan NextHunt;

    /// <summary>The linked server's research points, spent on the fee before the bank.</summary>
    public readonly int Points;

    /// <summary>Technologies the linked server knows.</summary>
    public readonly HashSet<string> Known;

    /// <summary>Disciplines the linked server covers; the tree shows their technologies.</summary>
    public readonly List<string> Disciplines;

    /// <summary>Powered decoding arrays linked to the server; each speeds up decoding.</summary>
    public readonly int Arrays;

    public WGBlueprintLocatorState(bool linked, string? serverName, List<WGBlueprintPatternState> patterns, int[] fees,
        int maxPatterns, WGBlueprintHuntState? hunt, TimeSpan nextHunt, int points, HashSet<string> known,
        List<string> disciplines, int arrays)
    {
        Arrays = arrays;
        Linked = linked;
        ServerName = serverName;
        Patterns = patterns;
        Fees = fees;
        MaxPatterns = maxPatterns;
        Hunt = hunt;
        NextHunt = nextHunt;
        Points = points;
        Known = known;
        Disciplines = disciplines;
    }
}

/// <summary>Starts a hunt for the chosen patterns, all of one tier.</summary>
[Serializable, NetSerializable]
public sealed class WGBlueprintLocateMessage(List<string> patterns) : BoundUserInterfaceMessage
{
    public readonly List<string> Patterns = patterns;
}

/// <summary>Gives up the running hunt.</summary>
[Serializable, NetSerializable]
public sealed class WGBlueprintAbandonMessage : BoundUserInterfaceMessage;

/// <summary>A shard finishes decoding into a Construct.</summary>
[Serializable, NetSerializable]
public sealed partial class WGBlueprintDecodeDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Decoding speed from decoding arrays.</summary>
public static class WGBlueprintDecode
{
    /// <summary>Decode time multiplier: each array cuts a quarter of what is left, down to 40%.</summary>
    public static float Scale(int arrays)
    {
        return MathF.Max(0.4f, MathF.Pow(0.75f, arrays));
    }
}
