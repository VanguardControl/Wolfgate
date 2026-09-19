using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// How good a world's deep-vein table is. This is the ONLY vein information the survey console's state ever carries:
/// never a weight, never an ore id and never a yield number.
/// </summary>
[Serializable, NetSerializable]
public enum WFVeinRating : byte
{
    /// <summary>Below the bands document's Fair threshold.</summary>
    Poor,

    /// <summary>At or past Fair.</summary>
    Fair,

    /// <summary>At or past Rich.</summary>
    Rich,

    /// <summary>At or past VeryRich.</summary>
    VeryRich,
}
