using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// How good a world's deep-vein table is; the only vein information the survey console state carries.
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
