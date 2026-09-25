namespace Content.Shared._WF.PlanetCracker.Anchors;

/// <summary>Shared maths for gravity anchor pairs; declares no subscriptions, the server half owns the behaviour.</summary>
public abstract partial class SharedWFGravityAnchorSystem : EntitySystem
{
    /// <summary>Cut radius for a pair: half the centre distance plus the padding.</summary>
    public static float GetCutRadius(float distance, float padding) => distance / 2f + padding;

    /// <summary>True when the two centres are inside the pairing band.</summary>
    public static bool InBand(float distance, float min, float max) => distance >= min && distance <= max;

    /// <summary>True for states where the anchor is drilled in and must not be unwrenched.</summary>
    // Off counts as armed: only a lapsed disconnect window or the wfcracker rearm command recovers it.
    public static bool IsArmed(WFAnchorState s) => s is WFAnchorState.Drilling or WFAnchorState.Locked or WFAnchorState.Off;
}
