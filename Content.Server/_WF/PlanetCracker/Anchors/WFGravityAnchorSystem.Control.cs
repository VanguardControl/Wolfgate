using Content.Shared._WF.PlanetCracker.Anchors;

namespace Content.Server._WF.PlanetCracker.Anchors;

/// <summary>
/// The public surface the admin command drives the anchors through, so it reaches the private state writer without
/// duplicating its guards or its side effects. No subscriptions.
/// </summary>
public sealed partial class WFGravityAnchorSystem
{
    /// <summary>
    /// Arms the unattended drill on a paired anchor: StartDrill without the verb's unused user.
    /// It exists because NO admin or test path raises WFAnchorDrillStartedEvent at all today - the verb is the only
    /// caller of StartDrill, and both `wfcracker complete drill` and the fixture's DeployPair go straight to
    /// <see cref="CompleteDrill"/>, which raises only WFAnchorDrillFinishedEvent.
    /// </summary>
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

    /// <summary>
    /// Finishes a drill at once: the same lock, thunk and event the 1 Hz sweep would have raised when the timer ran out.
    /// </summary>
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

    /// <summary>
    /// Switches a locked anchor off without raising the cancellable attempt, so a later veto cannot refuse an admin.
    /// </summary>
    public bool ForceSwitchOff(Entity<WFGravityAnchorComponent> ent)
    {
        if (ent.Comp.State != WFAnchorState.Locked)
            return false;

        SetState(ent, WFAnchorState.Off);

        var ev = new WFAnchorSwitchedOffEvent(ent.Owner);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>
    /// Puts a switched-off anchor back to Locked; the only exit from Off other than the pair dissolving.
    /// A lapsed disconnect pairing window and the admin command are the only callers: there is no player-facing re-arm.
    /// </summary>
    public bool ReArm(Entity<WFGravityAnchorComponent> ent)
    {
        if (ent.Comp.State != WFAnchorState.Off)
            return false;

        // Locked without a partner is not a state the pairing code can recover from, and Demote would have taken this
        // anchor out of Off already had the pair gone.
        if (ent.Comp.Partner is null)
            return false;

        SetState(ent, WFAnchorState.Locked);

        var ev = new WFAnchorReArmedEvent(ent.Owner);
        RaiseLocalEvent(ref ev);
        return true;
    }
}
