namespace Content.Shared._WF.PlanetCracker.Anchors;

/// <summary>Shared maths for gravity anchor pairs; declares no subscriptions, the server half owns the behaviour.</summary>
public abstract partial class SharedWFGravityAnchorSystem : EntitySystem
{
    /// <summary>Cut radius for a pair, per design D21: half the centre distance plus the padding.</summary>
    public static float GetCutRadius(float distance, float padding) => distance / 2f + padding;

    /// <summary>True when the two centres are inside the pairing band.</summary>
    public static bool InBand(float distance, float min, float max) => distance >= min && distance <= max;

    /// <summary>True for states where the anchor is drilled in and must not be unwrenched.</summary>
    // Off is armed and has no player-facing exit at all: an Off anchor is recovered automatically when a disconnect
    // pairing window lapses, or by hand through the wfcracker rearm command, and the state-aware unwrench refusal
    // now lives in WFGravityAnchorSystem.OnUnanchorAttempt rather than being promised here.
    public static bool IsArmed(WFAnchorState s) => s is WFAnchorState.Drilling or WFAnchorState.Locked or WFAnchorState.Off;
}
