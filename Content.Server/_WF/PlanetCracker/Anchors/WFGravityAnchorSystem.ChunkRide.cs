using Content.Shared._WF.PlanetCracker.Anchors;

namespace Content.Server._WF.PlanetCracker.Anchors;

/// <summary>
/// The chunk-ride suppression set and the crack ring's progress value. No subscriptions.
/// Extraction moves a deployed anchor onto the chunk grid, which is an unanchor and a re-anchor; without the set the
/// anchor handler would dissolve the pair on the way out and fail TryGetPlanetGround on the way back in, aborting the
/// cut thirty seconds after a successful extraction.
/// </summary>
public sealed partial class WFGravityAnchorSystem
{
    /// <summary>Ring movement worth a state send; below this a twelve minute cut would dirty an anchor every tick.</summary>
    private const float ProgressEpsilon = 0.01f;

    /// <summary>Anchors currently being moved onto a chunk grid; the anchor state handler ignores them.</summary>
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

    /// <summary>
    /// Writes the cut's progress onto an anchor, which is where the growing ring reads it: the anchors carry a global
    /// PVS override and the hull grid does not, so this is the only crack value a surface viewer ever receives.
    /// The endpoints always land exactly, so an idle pair reads 1 and a fresh cut reads 0 no matter how coarse the
    /// epsilon in between is.
    /// </summary>
    public void SetCrackProgress(Entity<WFGravityAnchorComponent> ent, float progress)
    {
        var clamped = Math.Clamp(progress, 0f, 1f);

        if (clamped == ent.Comp.CrackProgress)
            return;

        if (clamped is > 0f and < 1f && MathF.Abs(clamped - ent.Comp.CrackProgress) < ProgressEpsilon)
            return;

        ent.Comp.CrackProgress = clamped;
        Dirty(ent);
    }
}
