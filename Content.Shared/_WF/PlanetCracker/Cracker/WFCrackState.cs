using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>Stage of one planet crack, per design section 3.</summary>
[Serializable, NetSerializable]
public enum WFCrackState : byte
{
    /// <summary>Nothing under way.</summary>
    Idle,

    /// <summary>Looking for a site from the survey console.</summary>
    Surveying,

    /// <summary>At least one anchor is wrenched down on the surface.</summary>
    AnchorsPlaced,

    /// <summary>A pair has finished drilling and is targetable.</summary>
    AnchorsLocked,

    /// <summary>The projector is cutting the chunk free.</summary>
    Cracking,

    /// <summary>The chunk is cut and held by the centrifuge.</summary>
    Cracked,

    /// <summary>The ship is shedding its hold on the chunk.</summary>
    Disconnecting,

    /// <summary>The chunk is released and no longer held.</summary>
    Released,

    /// <summary>The released chunk is falling.</summary>
    Falling,
}
