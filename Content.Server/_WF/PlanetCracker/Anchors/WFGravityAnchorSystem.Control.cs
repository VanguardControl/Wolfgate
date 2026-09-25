using Content.Shared._WF.PlanetCracker.Anchors;

namespace Content.Server._WF.PlanetCracker.Anchors;

/// <summary>Admin entry points into the anchor state writer, with its guards and side effects.</summary>
public sealed partial class WFGravityAnchorSystem
{
    /// <summary>Arms the drill on a paired anchor without a user and raises WFAnchorDrillStartedEvent.</summary>
    public bool BeginDrill(Entity<WFGravityAnchorComponent> ent)
    {
        if (ent.Comp.State != WFAnchorState.Paired)
            return false;

        ent.Comp.DrillEnd = _timing.CurTime + ent.Comp.DrillDuration;
        SetState(ent, WFAnchorState.Drilling);

        var ev = new WFAnchorDrillStartedEvent(ent.Owner);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Finishes a drill at once, as the 1 Hz sweep would when the timer runs out.</summary>
    public bool CompleteDrill(Entity<WFGravityAnchorComponent> ent)
    {
        if (ent.Comp.State != WFAnchorState.Drilling && ent.Comp.State != WFAnchorState.Paired)
            return false;

        // Pulled forward so the sweep cannot fire a second WFAnchorDrillFinishedEvent for the same drill.
        ent.Comp.DrillEnd = _timing.CurTime;

        SetState(ent, WFAnchorState.Locked);
        _audio.PlayPvs(LockSound, ent.Owner);

        var ev = new WFAnchorDrillFinishedEvent(ent.Owner);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Switches a locked anchor off without the cancellable attempt, so no veto can refuse an admin.</summary>
    public bool ForceSwitchOff(Entity<WFGravityAnchorComponent> ent)
    {
        if (ent.Comp.State != WFAnchorState.Locked)
            return false;

        SetState(ent, WFAnchorState.Off);

        var ev = new WFAnchorSwitchedOffEvent(ent.Owner);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Puts a switched-off anchor back to Locked, for a lapsed disconnect window or an admin.</summary>
    public bool ReArm(Entity<WFGravityAnchorComponent> ent)
    {
        if (ent.Comp.State != WFAnchorState.Off)
            return false;

        // Locked without a partner can't be recovered; Demote would already have left Off had the pair gone.
        if (ent.Comp.Partner is null)
            return false;

        SetState(ent, WFAnchorState.Locked);

        var ev = new WFAnchorReArmedEvent(ent.Owner);
        RaiseLocalEvent(ref ev);
        return true;
    }
}
