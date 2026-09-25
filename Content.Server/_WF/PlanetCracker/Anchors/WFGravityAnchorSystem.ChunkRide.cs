using Content.Shared._WF.PlanetCracker.Anchors;

namespace Content.Server._WF.PlanetCracker.Anchors;

/// <summary>Chunk-ride suppression and the crack ring's progress value.</summary>
public sealed partial class WFGravityAnchorSystem
{
    /// <summary>Ring movement worth a state send; below this a twelve minute cut would dirty an anchor every tick.</summary>
    private const float ProgressEpsilon = 0.01f;

    /// <summary>Anchors riding onto a chunk grid; the anchor handler ignores them so the pair survives.</summary>
    private readonly HashSet<EntityUid> _riding = new();

    /// <summary>Suppresses the anchor handler for one anchor about to be moved onto a chunk grid.</summary>
    public void BeginChunkRide(EntityUid anchor)
    {
        _riding.Add(anchor);
    }

    /// <summary>Ends the suppression once both halves of the move are done.</summary>
    public void EndChunkRide(EntityUid anchor)
    {
        _riding.Remove(anchor);
    }

    /// <summary>Whether this anchor is mid-ride onto a chunk grid.</summary>
    public bool IsRidingChunk(EntityUid anchor)
    {
        return _riding.Contains(anchor);
    }

    /// <summary>Writes the cut's progress onto an anchor, the only crack value surface viewers receive.</summary>
    public void SetCrackProgress(Entity<WFGravityAnchorComponent> ent, float progress)
    {
        var clamped = Math.Clamp(progress, 0f, 1f);

        if (clamped == ent.Comp.CrackProgress)
            return;

        // The endpoints always land exactly; only the steps between are throttled.
        if (clamped is > 0f and < 1f && MathF.Abs(clamped - ent.Comp.CrackProgress) < ProgressEpsilon)
            return;

        ent.Comp.CrackProgress = clamped;
        Dirty(ent);
    }
}
