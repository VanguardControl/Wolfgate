using System.Numerics;
using Content.Server._NF.Shuttles.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Gravity;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>
/// Targeting, the clear-area check, the berth snap and the force-anchor lock. Every refusal here is a locale key the
/// caller shows: the checks must precede the state transition, because design D23 means there is no way back out.
/// </summary>
public sealed partial class WFCrackerSystem
{
    /// <summary>Targets the hull's locked pair so the cut has something to aim at.</summary>
    public bool TryTarget(Entity<WFPlanetCrackerComponent> ent, out string? reason)
    {
        reason = null;

        if (ent.Comp.State != WFCrackState.AnchorsLocked)
        {
            reason = "wf-crack-console-blocker-wrong-state";
            return false;
        }

        if (!TryGetOwnedPair(ent, out var a, out var b, true))
        {
            reason = "wf-crack-console-blocker-no-pair";
            return false;
        }

        if (!TryGetBerthOffset(ent, a, b, out var offset) || offset.Length() > ent.Comp.AlignTolerance)
        {
            reason = "wf-crack-console-refuse-alignment";
            return false;
        }

        // Checked here and again at begin time: the pilot is free to fly the hull anywhere in between.
        if (!IsDestinationClear(ent, offset))
        {
            reason = "wf-crack-console-refuse-obstructed";
            return false;
        }

        ent.Comp.AnchorA = GetNetEntity(a.Owner);
        ent.Comp.AnchorB = GetNetEntity(b.Owner);
        Dirty(ent);
        return true;
    }

    /// <summary>
    /// Drops the target. Legal only in AnchorsLocked (design D23): once the cut has begun there is no abort, which is
    /// also why no abort message exists in the shared vocabulary at all.
    /// </summary>
    public bool TryUntarget(Entity<WFPlanetCrackerComponent> ent, out string? reason)
    {
        reason = null;

        if (ent.Comp.State != WFCrackState.AnchorsLocked)
        {
            reason = "wf-crack-console-refuse-no-abort";
            return false;
        }

        ClearTarget(ent);
        return true;
    }

    /// <summary>Begins the cut: snap, then lock, then arm the timers. Refuses with a reason if anything is blocking.</summary>
    public bool TryBegin(Entity<WFPlanetCrackerComponent> ent, out string? reason)
    {
        reason = null;

        var blockers = ComputeBlockers(ent);
        ent.Comp.Blockers = blockers;

        if (blockers != WFCrackBlocker.None)
        {
            reason = GetBlockerReason(blockers);
            return false;
        }

        if (!TryGetTargetedPair(ent, out var a, out var b))
        {
            reason = "wf-crack-console-blocker-not-targeted";
            return false;
        }

        // Refuse external ownership before the snap can move a mapper's anchored hull.
        if (HasComp<ForceAnchorComponent>(ent.Owner))
        {
            reason = "wf-crack-console-refuse-external-lock";
            return false;
        }

        // Trap (b): the snap comes first. A force-anchored hull is a static body, and a static body makes its own CE
        // grid network static-anchored, at which point the sync system pins every move straight back.
        if (!TrySnapToCircle(ent, out reason))
            return false;

        if (!EngageLock(ent, out reason))
            return false;

        StartCrack(ent, a, b);
        return true;
    }

    /// <summary>Everything that would refuse a BEGIN CRACK right now; one flag per distinct fault.</summary>
    public WFCrackBlocker ComputeBlockers(Entity<WFPlanetCrackerComponent> ent)
    {
        var blockers = WFCrackBlocker.None;

        if (ent.Comp.State != WFCrackState.AnchorsLocked)
            blockers |= WFCrackBlocker.WrongState;

        if (!TryGetOwnedPair(ent, out var a, out var b, true))
        {
            blockers |= WFCrackBlocker.NoPair;
        }
        else
        {
            if (!TryGetTargetedPair(ent, out _, out _))
                blockers |= WFCrackBlocker.NotTargeted;

            if (!TryGetBerthOffset(ent, a, b, out var offset) || offset.Length() > ent.Comp.AlignTolerance)
                blockers |= WFCrackBlocker.NotAligned;
            else if (!IsDestinationClear(ent, offset))
                blockers |= WFCrackBlocker.Obstructed;
        }

        // Trap (a): a network of two or more grids cannot be moved at all, so it blocks the button rather than
        // failing halfway through the snap.
        if (_zLevels.TryGetGridNetwork(ent.Owner, out var network) && network.Comp.Grids.Count >= 2)
            blockers |= WFCrackBlocker.InGridNetwork;

        if (!TryGetCentrifuge(ent.Owner, out var centrifuge))
            blockers |= WFCrackBlocker.CentrifugeMissing;
        else if (!centrifuge.Comp.AtFull)
            blockers |= WFCrackBlocker.CentrifugeNotFull;

        GetProjectors(ent.Owner, _projectorBuffer);

        if (_projectorBuffer.Count < ent.Comp.RequiredProjectors)
            blockers |= WFCrackBlocker.ProjectorsShort;

        foreach (var projector in _projectorBuffer)
        {
            // Read from Broken and the power receiver, never from WFProjectorState, which is a display value the
            // projector's own power and repair handlers overwrite without any crack awareness.
            if (projector.Comp.Broken)
                blockers |= WFCrackBlocker.ProjectorsBroken;

            if (!_receiver.IsPowered(projector.Owner))
                blockers |= WFCrackBlocker.ProjectorsUnpowered;
        }

        // Deliberately outside the owned-pair branch: a planet that has already been cut refuses whatever state the
        // anchors are in, and the flag lives on the sector body so it outlives the z-network.
        if (IsPlanetCracked(ent))
            blockers |= WFCrackBlocker.PlanetCracked;

        return blockers;
    }

    /// <summary>The locale key for the first blocker worth naming, so a refusal popup says what is actually wrong.</summary>
    public static string GetBlockerReason(WFCrackBlocker blockers)
    {
        // First of all of them: it is the only permanent fault in the list, so naming anything else would send the
        // crew off to fix something that cannot help.
        if ((blockers & WFCrackBlocker.PlanetCracked) != 0)
            return "wf-crack-console-blocker-planet-cracked";

        if ((blockers & WFCrackBlocker.InGridNetwork) != 0)
            return "wf-crack-console-refuse-network";

        if ((blockers & WFCrackBlocker.Obstructed) != 0)
            return "wf-crack-console-refuse-obstructed";

        if ((blockers & WFCrackBlocker.NotAligned) != 0)
            return "wf-crack-console-refuse-alignment";

        if ((blockers & WFCrackBlocker.WrongState) != 0)
            return "wf-crack-console-blocker-wrong-state";

        if ((blockers & WFCrackBlocker.NoPair) != 0)
            return "wf-crack-console-blocker-no-pair";

        if ((blockers & WFCrackBlocker.NotTargeted) != 0)
            return "wf-crack-console-blocker-not-targeted";

        if ((blockers & WFCrackBlocker.CentrifugeMissing) != 0)
            return "wf-crack-console-blocker-centrifuge-missing";

        if ((blockers & WFCrackBlocker.CentrifugeNotFull) != 0)
            return "wf-crack-console-blocker-centrifuge-not-full";

        if ((blockers & WFCrackBlocker.ProjectorsShort) != 0)
            return "wf-crack-console-blocker-projectors-short";

        if ((blockers & WFCrackBlocker.ProjectorsBroken) != 0)
            return "wf-crack-console-blocker-projectors-broken";

        if ((blockers & WFCrackBlocker.ProjectorsUnpowered) != 0)
            return "wf-crack-console-blocker-projectors-unpowered";

        return "wf-crack-console-blocker-wrong-state";
    }

    /// <summary>
    /// Whether the hull's footprint at its snapped destination is free of everything but itself and what it is docked
    /// to. Refused hard rather than searched around: the snap is at most AlignTolerance tiles and a pilot already flew
    /// the hull there.
    /// </summary>
    public bool IsDestinationClear(Entity<WFPlanetCrackerComponent> ent, Vector2 offset)
    {
        if (!TryComp<MapGridComponent>(ent.Owner, out var grid))
            return false;

        var xform = Transform(ent.Owner);

        if (xform.MapID == MapId.Nullspace)
            return false;

        var (worldPos, worldRot) = TransformSystem.GetWorldPositionRotation(xform);
        var target = worldPos + offset;
        var hullBox = new Box2Rotated(grid.LocalAABB.Translated(target), worldRot, target);

        _found.Clear();
        _mapManager.FindGridsIntersecting(xform.MapID, hullBox, ref _found, approx: false, includeMap: false);

        if (_found.Count == 0)
            return true;

        _docked.Clear();
        _shuttle.GetAllDockedShuttles(ent.Owner, _docked);

        foreach (var found in _found)
        {
            if (found.Owner == ent.Owner || _docked.Contains(found.Owner))
                continue;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Slides the hull the last few tiles so its berth centre sits over the cut circle, taking its docked set with it.
    /// Refuses instead of half-moving: every check here has to pass before the state machine advances.
    /// </summary>
    public bool TrySnapToCircle(Entity<WFPlanetCrackerComponent> ent, out string? reason)
    {
        reason = null;

        // Trap (a): CEZGridSyncSystem intercepts any transform move of a networked grid and its only escape is a
        // network of fewer than two grids. With a static anchor it re-applies the old pose while the state machine
        // would have advanced anyway, so a linked hull is refused rather than nudged.
        if (_zLevels.TryGetGridNetwork(ent.Owner, out var network) && network.Comp.Grids.Count >= 2)
        {
            reason = "wf-crack-console-refuse-network";
            return false;
        }

        if (!TryGetTargetedPair(ent, out var a, out var b) || !TryGetBerthOffset(ent, a, b, out var offset))
        {
            reason = "wf-crack-console-blocker-not-targeted";
            return false;
        }

        if (!IsDestinationClear(ent, offset))
        {
            reason = "wf-crack-console-refuse-obstructed";
            return false;
        }

        // Trap (e): locking a hull we could never release is worse than refusing to lock it. See EngageLock.
        if (!HasComp<ShuttleComponent>(ent.Owner))
        {
            reason = "wf-crack-console-refuse-no-shuttle";
            return false;
        }

        var xform = Transform(ent.Owner);
        var (worldPos, worldRot) = TransformSystem.GetWorldPositionRotation(xform);

        // Trap (c): capture every docked grid's pose relative to ours before the move. The dock joint is a soft weld,
        // so a docked transport left where it was gets dragged across the map over about a second.
        _docked.Clear();
        _shuttle.GetAllDockedShuttles(ent.Owner, _docked);
        _dockedPoses.Clear();

        foreach (var docked in _docked)
        {
            if (docked == ent.Owner)
                continue;

            var dockedPos = TransformSystem.GetWorldPosition(docked);
            var dockedRot = TransformSystem.GetWorldRotation(docked);

            _dockedPoses[docked] = ((-worldRot).RotateVec(dockedPos - worldPos), dockedRot - worldRot);
        }

        if (TryComp<PhysicsComponent>(ent.Owner, out var body))
        {
            _physics.SetLinearVelocity(ent.Owner, Vector2.Zero, body: body);
            _physics.SetAngularVelocity(ent.Owner, 0f, body: body);
        }

        // A grid never re-parents on a world move (the re-parent branch is guarded on GridUid != uid), and the grid
        // enrols itself in MovedGrids, so physics and PVS need nothing by hand.
        TransformSystem.SetWorldPosition(ent.Owner, worldPos + offset);

        var newPos = TransformSystem.GetWorldPosition(ent.Owner);
        var newRot = TransformSystem.GetWorldRotation(ent.Owner);

        foreach (var (docked, pose) in _dockedPoses)
        {
            if (xform.MapUid is { } map)
                TransformSystem.SetParent(docked, map);

            TransformSystem.SetWorldRotationNoLerp(docked, newRot + pose.Rotation);
            TransformSystem.SetWorldPosition(docked, newPos + newRot.RotateVec(pose.Position));

            if (TryComp<PhysicsComponent>(docked, out var dockedBody))
            {
                _physics.SetLinearVelocity(docked, Vector2.Zero, body: dockedBody);
                _physics.SetAngularVelocity(docked, 0f, body: dockedBody);
            }

            _dock.RedockDocks(docked);
        }

        // The connector resolves its links by world position and subscribes map init, anchoring, tiles, splits and
        // termination - but never MoveEvent - so a moved grid has to ask for the recalculation itself.
        _connectors.MarkDirty();

        // StartGridShake needs a GravityComponent and silently does nothing without one.
        if (TryComp<GravityComponent>(ent.Owner, out var gravity))
            _gravity.StartGridShake(ent.Owner, gravity);

        _audio.PlayPvs(ent.Comp.LockSound, ent.Owner);
        return true;
    }

    /// <summary>
    /// Force-anchors the hull for the cut.
    /// Trap (e): ShuttleSystem.Enable resolves ShuttleComponent as a required dependency and returns silently without
    /// one, while Disable does not, so a hull with no ShuttleComponent would anchor and never release - no log, a dead
    /// abort path and a fall that never starts. It is refused instead.
    /// </summary>
    public bool EngageLock(Entity<WFPlanetCrackerComponent> ent, out string? reason)
    {
        reason = null;

        if (!HasComp<ShuttleComponent>(ent.Owner))
        {
            reason = "wf-crack-console-refuse-no-shuttle";
            Log.Error($"Refused to force-anchor {ToPrettyString(ent.Owner)} for a crack: it has no ShuttleComponent, so it could never be released.");
            return false;
        }

        // A mapper's own ForceAnchor is external ownership. Refuse rather than leaving Locked clear and later
        // releasing the mapper's anchor as though this system owned it.
        if (HasComp<ForceAnchorComponent>(ent.Owner))
        {
            reason = "wf-crack-console-refuse-external-lock";
            return false;
        }

        // Adding the component re-raises MapInitEvent on a map-initialised entity, which runs ForceAnchorSystem's
        // Disable(force: true) plus PreventGridAnchorChanges. A grid that never map-initialised gets no such event, so
        // the same pin is applied here by hand; both are idempotent.
        AddComp<ForceAnchorComponent>(ent.Owner);
        PinStatic(ent.Owner);
        ent.Comp.Locked = true;
        Dirty(ent);
        return true;
    }

    /// <summary>The force-anchor pin itself: a static body that nothing un-forced can re-enable.</summary>
    private void PinStatic(EntityUid uid)
    {
        _shuttle.Disable(uid, force: true);
        EnsureComp<PreventGridAnchorChangesComponent>(uid);
    }

    /// <summary>
    /// Re-asserts a lock this system applied. Nothing is supposed to re-enable a pinned hull, so a hull found moving
    /// again is pinned back and logged loudly: the log line is the only evidence of whichever path let it go.
    /// </summary>
    public void ReassertLock(Entity<WFPlanetCrackerComponent> ent)
    {
        if (!ent.Comp.Locked || !TryComp<PhysicsComponent>(ent.Owner, out var body))
            return;

        if (body.BodyType == BodyType.Static && HasComp<PreventGridAnchorChangesComponent>(ent.Owner))
            return;

        Log.Warning($"{ToPrettyString(ent.Owner)} was {body.BodyType} with its crack lock on (anchor changes prevented: {HasComp<PreventGridAnchorChangesComponent>(ent.Owner)}); pinned again.");
        PinStatic(ent.Owner);
    }

    /// <summary>
    /// Releases a lock this system applied. ForceAnchorSystem has no release path of its own, so this is it, including
    /// the trap (e) post-condition: the body must actually be Dynamic afterwards or the fall would never move.
    /// </summary>
    public void ReleaseLock(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.Locked)
        {
            ent.Comp.Locked = false;
            Dirty(ent);

            RemComp<ForceAnchorComponent>(ent.Owner);
            RemComp<PreventGridAnchorChangesComponent>(ent.Owner);
            _shuttle.Enable(ent.Owner, force: true);
        }

        // Also diagnose an external lock applied during a cut: this system must never remove a mapper's lock.
        if (TryComp<PhysicsComponent>(ent.Owner, out var body) && body.BodyType == BodyType.Static)
            Log.Error($"{ToPrettyString(ent.Owner)} is still a static body after its crack lock was released; it will not fall.");
    }
}
