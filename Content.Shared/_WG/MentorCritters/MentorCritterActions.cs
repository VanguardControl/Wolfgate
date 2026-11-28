using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.MentorCritters;

/// <summary>Climb into an empty pocket of the asker.</summary>
public sealed partial class WGMentorCritterPocketActionEvent : InstantActionEvent
{
}

/// <summary>Climb onto the asker's head.</summary>
public sealed partial class WGMentorCritterHeadActionEvent : InstantActionEvent
{
}

/// <summary>Get out of the pocket or off the head.</summary>
public sealed partial class WGMentorCritterDownActionEvent : InstantActionEvent
{
}

/// <summary>Switch between talking to the asker alone and speaking aloud.</summary>
public sealed partial class WGMentorCritterAloudActionEvent : InstantActionEvent
{
}

/// <summary>Point something out to the asker; only they see it.</summary>
public sealed partial class WGMentorCritterPingActionEvent : WorldTargetActionEvent
{
}

/// <summary>The short climb into a pocket or onto the head.</summary>
[Serializable, NetSerializable]
public sealed partial class WGMentorCritterClimbDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public string Slot = "";
}
