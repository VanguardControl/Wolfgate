using Content.Shared._WF.PlanetCracker.Anchors;

namespace Content.Server._WF.PlanetCracker.Anchors;

/// <summary>Pair formation and dissolution, and the single writer for <see cref="WFGravityAnchorComponent.State"/>.</summary>
public sealed partial class WFGravityAnchorSystem
{
    /// <summary>Finds a compatible partner for a freshly deployed anchor and forms the pair.</summary>
    public bool TryPair(Entity<WFGravityAnchorComponent> anchor)
    {
        if (anchor.Comp.Partner != null || anchor.Comp.State != WFAnchorState.Deployed)
            return false;

        var xform = Transform(anchor.Owner);

        if (xform.GridUid is not { } grid)
            return false;

        var selfPos = _transform.GetWorldPosition(xform);

        EntityUid? bestUid = null;
        WFGravityAnchorComponent? bestComp = null;
        var bestDistance = float.MaxValue;

        var query = EntityQueryEnumerator<WFGravityAnchorComponent, TransformComponent>();
        while (query.MoveNext(out var otherUid, out var other, out var otherXform))
        {
            if (otherUid == anchor.Owner || other.Partner != null)
                continue;

            if (other.State is WFAnchorState.Loose or WFAnchorState.Broken)
                continue;

            // Same ground grid: stricter and cheaper than a map test, and identical on a ground layer.
            if (otherXform.GridUid != grid)
                continue;

            // Two owned anchors must share a cracker. An unowned anchor (a hand-spawned dev one, or one from the
            // transport's own crate, which is never aboard the cracker to be bound) pairs with anything and adopts
            // its partner's owner below, so the crack console still finds the pair.
            if (other.Cracker != null && anchor.Comp.Cracker != null && other.Cracker != anchor.Comp.Cracker)
                continue;

            var distance = (selfPos - _transform.GetWorldPosition(otherXform)).Length();

            if (!InBand(distance, anchor.Comp.MinDistance, anchor.Comp.MaxDistance) || distance >= bestDistance)
                continue;

            bestUid = otherUid;
            bestComp = other;
            bestDistance = distance;
        }

        if (bestUid is not { } partnerUid || bestComp == null)
            return false;

        anchor.Comp.Partner = GetNetEntity(partnerUid);
        bestComp.Partner = GetNetEntity(anchor.Owner);

        var owner = anchor.Comp.Cracker ?? bestComp.Cracker;
        anchor.Comp.Cracker = owner;
        bestComp.Cracker = owner;

        SetState(anchor, WFAnchorState.Paired);
        SetState((partnerUid, bestComp), WFAnchorState.Paired);

        var ev = new WFAnchorPairFormedEvent(anchor.Owner, partnerUid, bestDistance);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Breaks a pair and demotes whichever half survives.</summary>
    public void Dissolve(Entity<WFGravityAnchorComponent> anchor, bool touchSelf = true)
    {
        if (anchor.Comp.Partner is not { } netPartner)
            return;

        anchor.Comp.Partner = null;

        if (touchSelf)
            Dirty(anchor);

        var partnerUid = EntityUid.Invalid;

        if (TryGetEntity(netPartner, out var partner) &&
            TryComp<WFGravityAnchorComponent>(partner, out var partnerComp))
        {
            partnerUid = partner.Value;
            partnerComp.Partner = null;
            Dirty(partnerUid, partnerComp);
            Demote((partnerUid, partnerComp));
        }

        if (touchSelf)
            Demote(anchor);

        // Both halves can terminate in the same tick, so the second shutdown finds the first already deleted; the
        // event still fires with B = EntityUid.Invalid so subscribers can clean up A, and its summary says so.
        var ev = new WFAnchorPairDissolvedEvent(anchor.Owner, partnerUid);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>Drops a half that has lost its partner back to whatever its stance allows.</summary>
    private void Demote(Entity<WFGravityAnchorComponent> ent)
    {
        if (ent.Comp.State is not (WFAnchorState.Paired
            or WFAnchorState.Drilling
            or WFAnchorState.Locked
            or WFAnchorState.Off))
        {
            return;
        }

        ent.Comp.DrillEnd = TimeSpan.Zero;
        SetState(ent, Transform(ent.Owner).Anchored ? WFAnchorState.Deployed : WFAnchorState.Loose);
    }

    /// <summary>The only writer of the anchor state; it also pushes appearance and the drill ambience.</summary>
    private void SetState(Entity<WFGravityAnchorComponent> ent, WFAnchorState state)
    {
        ent.Comp.State = state;
        Dirty(ent);

        _appearance.SetData(ent.Owner, WFAnchorVisuals.State, state);
        _ambient.SetAmbience(ent.Owner, state == WFAnchorState.Drilling);
    }

    /// <summary>Sets the networked damage flag, pushes the overlay and announces the crossing.</summary>
    private void SetDamaged(Entity<WFGravityAnchorComponent> ent, bool damaged)
    {
        ent.Comp.Damaged = damaged;
        Dirty(ent);

        _appearance.SetData(ent.Owner, WFAnchorVisuals.Damaged, damaged);

        var ev = new WFAnchorDamagedEvent(ent.Owner, damaged);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>World distance in tiles to this anchor's partner, which may be out of PVS or already gone.</summary>
    public bool TryGetPairDistance(Entity<WFGravityAnchorComponent> ent, out float distance)
    {
        distance = 0f;

        if (ent.Comp.Partner is not { } netPartner || !TryGetEntity(netPartner, out var partner))
            return false;

        distance = (_transform.GetWorldPosition(Transform(ent.Owner)) - _transform.GetWorldPosition(Transform(partner.Value))).Length();
        return true;
    }
}
