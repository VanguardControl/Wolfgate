using Content.Server._NF.Shuttles.Components;
using Content.Server.Chat.Systems;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Content.Server._WF.PlanetCracker.Planets;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>
/// The disconnect protocol: the switch-off veto, the 60 s two-anchor pairing window, the evacuation countdown and the
/// release, all on the hull side. The chunk side reacts to the state edge and to WFCrackerReleasingEvent instead of
/// being called, because WFPlanetChunkSystem already depends on this system and the reverse would be a cycle.
/// Only three broadcast subscriptions are added, all in InitializeDisconnect; the only other subscribers of any of the
/// three in the tree are the integration test recorder at GravityAnchorTest.cs:1200, :1201 and :1212, and no directed
/// (component, event) pair is added anywhere in this file.
/// A projector or centrifuge fault during the 60 s evacuation deliberately no longer drops the hull: the chunk is
/// leaving regardless, so EnterDisconnecting disarms the grace countdown for good rather than carrying it over.
/// </summary>
public sealed partial class WFCrackerSystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFGridAudienceSystem _audience = default!;

    /// <summary>How long is left on the pairing window when the single "closing" popup fires.</summary>
    private static readonly TimeSpan DisconnectBeatAt = TimeSpan.FromSeconds(15);

    /// <summary>The two evacuation popup beats, in the order EvacBeat counts them off.</summary>
    private static readonly TimeSpan[] EvacBeatsAt = { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10) };

    /// <summary>Short retry delay when a chunk could not enter transit at release time.</summary>
    private static readonly TimeSpan ReleaseRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Hulls already reported as stuck in Released; the sweep is 1 Hz and that state is permanent.</summary>
    private readonly HashSet<EntityUid> _releaseStuckLogged = new();

    /// <summary>Registers the three disconnect events; called from the system's one Initialize override.</summary>
    private void InitializeDisconnect()
    {
        // The attempt is a plain sealed class : CancellableEntityEventArgs (WFAnchorEvents.cs:35) and so is subscribed
        // BY VALUE; the other two are [ByRefEvent] record structs. All three are broadcast, which is legal any number
        // of times over - the duplicate-subscription crash is a directed (component, event) rule only.
        SubscribeLocalEvent<WFAnchorSwitchOffAttemptEvent>(OnSwitchOffAttempt);
        SubscribeLocalEvent<WFAnchorSwitchedOffEvent>(OnSwitchedOff);
        SubscribeLocalEvent<WFChunkDroppedEvent>(OnChunkDropped);
    }

    /// <summary>Refuses a switch-off anywhere but a chunk-riding anchor of a hull that has finished its cut.</summary>
    private void OnSwitchOffAttempt(WFAnchorSwitchOffAttemptEvent args)
    {
        // A hand-spawned anchor belongs to nobody and is nobody's business: GravityAnchorTest deploys exactly that and
        // asserts the switch-off goes through, so this returns without cancelling rather than refusing on a null owner.
        if (!TryGetOwner(args.Anchor, out var cracker))
            return;

        // Every reason is already localised: SwitchOff substitutes it raw into wf-anchor-verb-off-refused.
        if (cracker.Comp.State != WFCrackState.Cracked)
        {
            args.Reason = Loc.GetString("wf-anchor-off-not-cracked");
            args.Cancel();
            return;
        }

        if (cracker.Comp.PendingAbort is not null)
        {
            args.Reason = Loc.GetString("wf-anchor-off-aborting");
            args.Cancel();
            return;
        }

        if (Transform(args.Anchor).GridUid is not { } grid || !HasComp<WFPlanetChunkComponent>(grid))
        {
            args.Reason = Loc.GetString("wf-anchor-off-not-on-chunk");
            args.Cancel();
        }
    }

    /// <summary>The pairing window: the first switch-off arms it, a second one on the other anchor commits the disconnect.</summary>
    private void OnSwitchedOff(ref WFAnchorSwitchedOffEvent args)
    {
        if (!TryGetOwner(args.Anchor, out var ent) || ent.Comp.State != WFCrackState.Cracked)
            return;

        // The veto's identical guard is not enough: ForceSwitchOff skips the cancellable attempt entirely and
        // `wfcracker disconnect` drives both anchors through it. Without this, an admin could enter Disconnecting
        // mid-spin-down, where the sweep's ungated PendingAbort arm would starve every evacuation branch and
        // FinishAbort - which has no state guard - would overwrite Disconnecting with AnchorsPlaced 30 s later.
        if (ent.Comp.PendingAbort is not null)
            return;

        var anchor = GetNetEntity(args.Anchor);

        if (!ent.Comp.DisconnectArmed)
        {
            ent.Comp.DisconnectAnchor = anchor;
            ent.Comp.DisconnectEnd = _timing.CurTime + ent.Comp.DisconnectWindow;
            ent.Comp.DisconnectArmed = true;
            ent.Comp.DisconnectBeat = 0;
            Dirty(ent);

            PopupOnGrid(
                GetChunkGrid(ent),
                "wf-crack-disconnect-armed",
                ("seconds", (int)ent.Comp.DisconnectWindow.TotalSeconds));
            return;
        }

        // Unreachable in practice - SwitchOff and ForceSwitchOff both require Locked, and the held anchor is Off - but
        // a re-raise for the same anchor must never be mistaken for the second half of the pair.
        if (ent.Comp.DisconnectAnchor == anchor)
            return;

        // No re-arm: the held anchor is meant to stay off, this is the commit.
        DisarmWindow(ent, false);
        EnterDisconnecting(ent);
    }

    /// <summary>Runs the pairing window down; called unconditionally from the sweep, before the abort branch.</summary>
    private void UpdateDisconnectWindow(Entity<WFPlanetCrackerComponent> ent)
    {
        if (!ent.Comp.DisconnectArmed)
            return;

        // Anything that moved the hull off Cracked - an abort landing, an admin state change - ends the window, and the
        // held anchor is put back rather than stranded in Off, which has no other exit.
        if (ent.Comp.State != WFCrackState.Cracked)
        {
            DisarmWindow(ent, true);
            return;
        }

        var chunk = GetChunkGrid(ent);

        if (ent.Comp.DisconnectBeat < 1 && ent.Comp.DisconnectEnd - _timing.CurTime <= DisconnectBeatAt)
        {
            ent.Comp.DisconnectBeat = 1;
            Dirty(ent);

            PopupOnGrid(chunk, "wf-crack-disconnect-warning", ("seconds", (int)DisconnectBeatAt.TotalSeconds));
        }

        if (_timing.CurTime < ent.Comp.DisconnectEnd)
            return;

        DisarmWindow(ent, true);
        PopupOnGrid(chunk, "wf-crack-disconnect-lapsed");
    }

    /// <summary>
    /// Cuts an evacuation the hull is no longer running; called unconditionally from the sweep beside
    /// UpdateDisconnectWindow, and for the same reason. ReleaseNow and EnterReleased both guard on Disconnecting and
    /// UpdateEvacuation is only ever reached from the sweep's Disconnecting arm, so an admin `wfcracker state` or
    /// `wfcracker fall` out of Disconnecting would otherwise strand EvacRunning true with the looping alarm playing for
    /// the rest of the round - PlayGlobal parents its audio in nullspace and skips TimedDespawn while looping, so it
    /// does not even die with the grid. Idempotent: the two release paths clear EvacRunning themselves.
    /// </summary>
    private void UpdateStrandedEvacuation(Entity<WFPlanetCrackerComponent> ent)
    {
        if (!ent.Comp.EvacRunning || ent.Comp.State == WFCrackState.Disconnecting)
            return;

        // EvacEnd is zeroed too: a hull put back into Disconnecting later would otherwise be released on its first
        // sweep against a deadline that expired while it was somewhere else.
        ent.Comp.EvacRunning = false;
        ent.Comp.EvacBeat = 0;
        ent.Comp.EvacEnd = TimeSpan.Zero;
        ent.Comp.EvacNextLoop = TimeSpan.Zero;
        StopHullAlarm(ent);
        Dirty(ent);
    }

    /// <summary>Clears the pairing window, optionally putting the anchor it was holding back to Locked.</summary>
    private void DisarmWindow(Entity<WFPlanetCrackerComponent> ent, bool reArm)
    {
        if (reArm && TryGetAnchor(ent.Comp.DisconnectAnchor, out var anchor) && !_anchors.ReArm(anchor))
            Log.Debug($"{ToPrettyString(anchor.Owner)} could not be re-armed as its disconnect window lapsed.");

        ent.Comp.DisconnectAnchor = null;
        ent.Comp.DisconnectEnd = TimeSpan.Zero;
        ent.Comp.DisconnectBeat = 0;
        ent.Comp.DisconnectArmed = false;
        Dirty(ent);
    }

    /// <summary>Admin and test entry point: drops an armed pairing window and re-arms the anchor holding it open.</summary>
    public void ReArmWindow(Entity<WFPlanetCrackerComponent> ent)
    {
        DisarmWindow(ent, true);
    }

    /// <summary>Commits the disconnect: the evacuation is armed, everything Cracked owned is disarmed, then the state moves.</summary>
    private void EnterDisconnecting(Entity<WFPlanetCrackerComponent> ent)
    {
        // Every timer is written BEFORE SetState, because SetState raises WFCrackStateChangedEvent synchronously and
        // the chunk system's handler reads EvacEnd straight off this component to build its own countdown.
        ent.Comp.EvacEnd = _timing.CurTime + ent.Comp.EvacDuration;
        ent.Comp.EvacRunning = true;
        ent.Comp.EvacBeat = 0;
        ent.Comp.EvacNextLoop = _timing.CurTime + ent.Comp.EvacReissue;

        // UpdateGrace only ever runs in the Cracking/Cracked sweep branch and nothing else stops the klaxon it armed,
        // so a grace still running as the hull leaves Cracked would loop for the rest of the round.
        StopKlaxon(ent);
        ent.Comp.GraceRunning = false;
        ent.Comp.GraceEnd = TimeSpan.Zero;
        ent.Comp.Failing = WFCrackFailure.None;

        // The sweep's PendingAbort arm is first in the chain and is not gated on state, so every Disconnecting and
        // Released branch below it is unreachable while a spin-down is pending; and FinishAbort has no state guard of
        // its own. StartAbort early-returns outside Cracking/Cracked, so clearing it here is final.
        ent.Comp.PendingAbort = null;
        ent.Comp.AbortEnd = TimeSpan.Zero;
        Dirty(ent);

        SetState(ent, WFCrackState.Disconnecting);

        StartHullAlarm(ent);
        Announce(ent.Owner, "wf-crack-disconnect-committed", ("seconds", (int)ent.Comp.EvacDuration.TotalSeconds));
    }

    /// <summary>The Disconnecting sweep branch: the hull's own beats, the alarm re-issue and the expiry.</summary>
    private void UpdateEvacuation(Entity<WFPlanetCrackerComponent> ent)
    {
        var remaining = ent.Comp.EvacEnd - _timing.CurTime;

        // The chunk fires its own beats with its own wording, so each grid is told what matters to the people on it.
        for (var beat = ent.Comp.EvacBeat; beat < EvacBeatsAt.Length; beat++)
        {
            if (remaining > EvacBeatsAt[beat])
                break;

            ent.Comp.EvacBeat = (byte)(beat + 1);
            Dirty(ent);

            PopupOnGrid(ent.Owner, "wf-crack-evac-warning", ("seconds", (int)EvacBeatsAt[beat].TotalSeconds));
        }

        // PlayGlobal freezes its recipient set at play time, so a latecomer boarding mid-countdown would never hear the
        // alarm; the loop is stopped and replayed on a cadence instead.
        if (_timing.CurTime >= ent.Comp.EvacNextLoop)
        {
            StartHullAlarm(ent);
            ent.Comp.EvacNextLoop = _timing.CurTime + ent.Comp.EvacReissue;
        }

        if (_timing.CurTime >= ent.Comp.EvacEnd)
            ReleaseNow(ent);
    }

    /// <summary>Lets the chunk go at once; the evacuation expiry, the admin `wfcracker release` and the tests share it.</summary>
    public void ReleaseNow(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.State != WFCrackState.Disconnecting)
            return;

        // The chunk system drops synchronously off this, which raises WFChunkDroppedEvent and so reaches EnterReleased
        // below before this call returns.
        var ev = new WFCrackerReleasingEvent(ent.Owner);
        RaiseLocalEvent(ref ev);

        // A chunk that is still owned and parked failed to enter transit. Keep the disconnect alive and retry through
        // the normal expiry path; DropChunk deliberately leaves the back-link intact on this failure.
        if (ent.Comp.State == WFCrackState.Disconnecting)
        {
            if (TryGetOwnedParkedChunk(ent))
            {
                RetryRelease(ent);
                return;
            }

            // Nothing dropped: an admin deleted the chunk, or the back-link never resolved. There is no owned chunk
            // left to retry, so the hull may still complete the release fallback.
            EnterReleased(ent);
        }
    }

    /// <summary>Re-arms the release attempt after transit admission failed, without claiming a successful release.</summary>
    private void RetryRelease(Entity<WFPlanetCrackerComponent> ent)
    {
        ent.Comp.EvacRunning = true;
        ent.Comp.EvacEnd = _timing.CurTime + ReleaseRetryDelay;
        // Keep the existing alarm and completed warning beats; retrying must not replay them every second.
        Dirty(ent);
    }

    /// <summary>Resolves the chunk backlink locally without coupling the cracker system to the chunk system.</summary>
    private bool TryGetOwnedParkedChunk(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.Chunk is not { } netChunk
            || !TryGetEntity(netChunk, out var chunk)
            || !TryComp<WFPlanetChunkComponent>(chunk, out var chunkComp))
        {
            return false;
        }

        return !chunkComp.Dropped;
    }

    /// <summary>A chunk going into transit is what actually ends the disconnect, whichever path pushed it.</summary>
    private void OnChunkDropped(ref WFChunkDroppedEvent args)
    {
        // The watchdog path carries EntityUid.Invalid when the hull the chunk was cut for has already gone.
        if (!args.Cracker.IsValid() || !TryComp<WFPlanetCrackerComponent>(args.Cracker, out var comp))
            return;

        if (comp.State != WFCrackState.Disconnecting)
            return;

        EnterReleased((args.Cracker, comp));
    }

    /// <summary>Hands the hull back: the lock comes off, the target is dropped and the settle timer starts.</summary>
    private void EnterReleased(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.State != WFCrackState.Disconnecting)
            return;

        ReleaseLock(ent);

        // ClearTarget MUST precede the chunk's deletion: it is what makes IsTargeted false, so OnAnchorBroken and
        // OnAnchorDestroyed return at their guard as both anchors terminate with the grid and no abort is ever started.
        ClearTarget(ent);

        ent.Comp.EvacRunning = false;
        ent.Comp.ReleaseEnd = _timing.CurTime + ent.Comp.ReleaseSettle;
        StopHullAlarm(ent);
        Dirty(ent);

        SetState(ent, WFCrackState.Released);
        Announce(ent.Owner, "wf-crack-released");
    }

    /// <summary>The Released sweep branch: the hull settles back to Idle, and the survey edge takes it from there.</summary>
    private void UpdateRelease(Entity<WFPlanetCrackerComponent> ent)
    {
        if (_timing.CurTime < ent.Comp.ReleaseEnd)
            return;

        // A hull force-anchored by a mapper sticks here for good, which is the D-M failure made visible. The sweep is
        // 1 Hz and the state is permanent, so the line is written once rather than every second for the rest of the round.
        if (ent.Comp.Locked || HasComp<ForceAnchorComponent>(ent.Owner))
        {
            if (_releaseStuckLogged.Add(ent.Owner))
                Log.Error($"{ToPrettyString(ent.Owner)} released its chunk but is still anchored; it is stuck in Released.");

            return;
        }

        _releaseStuckLogged.Remove(ent.Owner);
        SetState(ent, WFCrackState.Idle);
    }

    /// <summary>Starts (or restarts) the hull's evacuation alarm for everyone currently aboard it.</summary>
    private void StartHullAlarm(Entity<WFPlanetCrackerComponent> ent)
    {
        StopHullAlarm(ent);

        // PlayGlobal to a grid filter, never PlayPvs on the grid: that parents the audio at the grid's local origin
        // with a 15 tile default MaxDistance, which on a capital hull is a bridge-area klaxon and nothing more.
        ent.Comp.EvacStream = _audio.PlayGlobal(
            ent.Comp.EvacSound,
            _audience.Aboard(ent.Owner),
            true,
            AudioParams.Default.WithLoop(true).WithVolume(-4f))?.Entity;
    }

    /// <summary>Stops the hull's evacuation alarm loop.</summary>
    private void StopHullAlarm(Entity<WFPlanetCrackerComponent> ent)
    {
        ent.Comp.EvacStream = _audio.Stop(ent.Comp.EvacStream);
    }

    /// <summary>One red announcement to everyone on a grid; the arguments are formatted here, not by the chat system.</summary>
    private void Announce(EntityUid grid, string key, params (string, object)[] args)
    {
        if (!grid.IsValid())
            return;

        _chat.DispatchFilteredAnnouncement(
            _audience.Aboard(grid),
            Loc.GetString(key, args),
            sender: Loc.GetString("wf-crack-announce-sender"),
            playSound: false,
            colorOverride: Color.Red);
    }

    /// <summary>One caution popup per person on a grid, anchored on their own entity so it is never outside their PVS.</summary>
    private void PopupOnGrid(EntityUid grid, string key, params (string, object)[] args)
    {
        if (!grid.IsValid())
            return;

        var message = Loc.GetString(key, args);

        foreach (var player in _audience.Aboard(grid).Recipients)
        {
            if (player.AttachedEntity is not { } uid)
                continue;

            _popup.PopupEntity(message, uid, uid, PopupType.LargeCaution);
        }
    }

    /// <summary>The hull's chunk grid, or EntityUid.Invalid once the back-link has stopped resolving.</summary>
    private EntityUid GetChunkGrid(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.Chunk is not { } net || !TryGetEntity(net, out var chunk))
            return EntityUid.Invalid;

        return chunk.Value;
    }
}
