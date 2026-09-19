using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Mining;

/// <summary>What a crack miner is doing; the one axis its sprite is driven from.</summary>
/// <remarks>There is no Off member on purpose: crack_miner.rsi ships idle/mining/exhausted/broken and no off state, so idle doubles as off (ASSET_REQUIREMENTS.md:26 says otherwise and is stale).</remarks>
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
