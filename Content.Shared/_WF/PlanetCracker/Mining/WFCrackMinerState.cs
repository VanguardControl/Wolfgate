using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Mining;

/// <summary>What a crack miner is doing, which drives its sprite; Idle doubles as off since the sprite has no off state.</summary>
[Serializable, NetSerializable]
public enum WFCrackMinerState : byte
{
    /// <summary>Anchored, powered, no vein-blocking condition, waiting on its next tick.</summary>
    Idle,

    /// <summary>Drew charge and cut ore on its last tick.</summary>
    Mining,

    /// <summary>Its vein's Remaining has hit zero.</summary>
    Exhausted,

    /// <summary>Destructible Breakage threshold crossed; repairable.</summary>
    Broken,
}
