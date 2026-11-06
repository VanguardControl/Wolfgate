using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Blueprints;

[Serializable, NetSerializable]
public enum WFBlueprintLocatorUiKey : byte
{
    Key,
}

/// <summary>One pattern on the locator's list.</summary>
[Serializable, NetSerializable]
public sealed record WFBlueprintPatternState(string Id, bool Owned);

/// <summary>The locator's running hunt.</summary>
[Serializable, NetSerializable]
public sealed record WFBlueprintHuntState(string Site, string World, int Tier, List<string> Patterns, int Remaining);

[Serializable, NetSerializable]
public sealed class WFBlueprintLocatorState : BoundUserInterfaceState
{
    public readonly bool Linked;
    public readonly List<WFBlueprintPatternState> Patterns;
    public readonly int[] Fees;
    public readonly int MaxPatterns;
    public readonly WFBlueprintHuntState? Hunt;
    public readonly TimeSpan NextHunt;

    public WFBlueprintLocatorState(bool linked, List<WFBlueprintPatternState> patterns, int[] fees, int maxPatterns,
        WFBlueprintHuntState? hunt, TimeSpan nextHunt)
    {
        Linked = linked;
        Patterns = patterns;
        Fees = fees;
        MaxPatterns = maxPatterns;
        Hunt = hunt;
        NextHunt = nextHunt;
    }
}

/// <summary>Starts a hunt for the chosen patterns, all of one tier.</summary>
[Serializable, NetSerializable]
public sealed class WFBlueprintLocateMessage(List<string> patterns) : BoundUserInterfaceMessage
{
    public readonly List<string> Patterns = patterns;
}

/// <summary>Sent from the research console's patterns button: opens the locator window.</summary>
[Serializable, NetSerializable]
public sealed class WFBlueprintOpenLocatorMessage : BoundUserInterfaceMessage;

/// <summary>Gives up the running hunt.</summary>
[Serializable, NetSerializable]
public sealed class WFBlueprintAbandonMessage : BoundUserInterfaceMessage;

/// <summary>A shard finishes decoding into a Construct.</summary>
[Serializable, NetSerializable]
public sealed partial class WFBlueprintDecodeDoAfterEvent : SimpleDoAfterEvent;
