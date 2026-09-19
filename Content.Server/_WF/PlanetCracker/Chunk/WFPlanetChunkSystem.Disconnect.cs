using Content.Server._NF.Shuttles.Components;
using Content.Server.Chat.Systems;
using Content.Server.StationEvents.Events;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server._WF.PlanetCracker.Chunk;

/// <summary>
/// The chunk's half of the disconnect protocol: the evacuation alarm, the landing and the wreck cleanup.
/// Both subscriptions are BROADCAST and by ref. WFCrackStateChangedEvent is already subscribed broadcast at
/// WFCrackConsoleSystem.cs:57 and GravityAnchorTest.cs:1205, and WFCrackerReleasingEvent is the hull's release hook -
/// neither is a directed (component, event) pair, so neither can collide with the one-owner rule.
/// The handoff is events rather than a dependency because WFPlanetChunkSystem already depends on WFCrackerSystem.
/// </summary>
public sealed partial class WFPlanetChunkSystem
{
    /// <summary>
    /// How often the chunk's alarm loop is stopped and replayed. PlayGlobal freezes its recipient set at play time, so
    /// a latecomer boarding mid-countdown would otherwise hear nothing at all.
    /// </summary>
    private static readonly TimeSpan EvacReissue = TimeSpan.FromSeconds(15);

    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private LinkedLifecycleGridSystem _lifecycle = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFCrackScarSystem _scars = default!;

    /// <summary>
    /// Landed chunks the sweep decided to clean up, collected first because UnparentPlayersFromGrid deletes with a
    /// synchronous Del (LinkedLifecycleGridSystem.cs:195), which would invalidate the query enumerator inline.
    /// </summary>
    private readonly List<Entity<WFPlanetChunkComponent>> _cleanupBuffer = new();

    /// <summary>
    /// How many pre-drop release beats each evacuating chunk has already fired, so a re-entered sweep cannot repeat one.
    /// Kept server-side rather than on the component: the chunk's wording differs from the hull's and the hull's own
    /// EvacBeat counter belongs to WFCrackerSystem's sweep.
    /// </summary>
    private readonly Dictionary<EntityUid, byte> _evacBeats = new();

    /// <summary>Exactly two subscriptions, both broadcast and both by ref; no directed pair is added.</summary>
    private void InitializeDisconnect()
    {
        SubscribeLocalEvent<WFCrackStateChangedEvent>(OnCrackStateChanged);
        SubscribeLocalEvent<WFCrackerReleasingEvent>(OnCrackerReleasing);
    }

    /// <summary>Arms the chunk's alarm on the Disconnecting edge, and stops it if an admin pulls the hull back out.</summary>
    private void OnCrackStateChanged(ref WFCrackStateChangedEvent args)
    {
        if (args.New == WFCrackState.Disconnecting)
        {
            // EnterDisconnecting writes every timer field before SetState, so EvacDuration is already the live one.
            if (TryComp<WFPlanetCrackerComponent>(args.Cracker, out var cracker)
                && TryGetChunk((args.Cracker, cracker), out var chunk))
            {
                StartEvacuation(chunk, "wf-chunk-evac-announce", (int) cracker.EvacDuration.TotalSeconds);
            }

            return;
        }

        if (args.Old != WFCrackState.Disconnecting)
            return;

        // The escape branch, and the ONLY case it covers: an admin `wfcracker state` pulling the hull out of
        // Disconnecting before any drop happened. It cannot use TryGetChunk, because DropChunk nulls the hull's
        // back-link at WFPlanetChunkSystem.cs:229-236 BEFORE raising the WFChunkDroppedEvent that drives
        // Disconnecting -> Released, so on the normal path the back-link is already gone by now. The normal stop is
        // OnLanded.
        var query = EntityQueryEnumerator<WFPlanetChunkComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Dropped
                || comp.Cracker is not { } net
                || !TryGetEntity(net, out var owner)
                || owner.Value != args.Cracker)
            {
                continue;
            }

            StopEvacuation((uid, comp));
            break;
        }
    }

    /// <summary>The evacuation ran out, so the chunk goes. Exactly the shape of OnCrackerFalling.</summary>
    private void OnCrackerReleasing(ref WFCrackerReleasingEvent args)
    {
        if (!TryComp<WFPlanetCrackerComponent>(args.Cracker, out var cracker))
            return;

        if (!TryGetChunk((args.Cracker, cracker), out var chunk))
            return;

        DropChunk(chunk);
    }

    /// <summary>
    /// Starts the chunk's own alarm, announcement and popup. The announcement key is a parameter because the two
    /// callers say different things: the disconnect edge counts down, the watchdog reports a chunk with no cracker.
    /// </summary>
    public void StartEvacuation(Entity<WFPlanetChunkComponent> ent, string announceKey, int seconds)
    {
        if (ent.Comp.Evacuating)
            return;

        ent.Comp.Evacuating = true;
        ent.Comp.EvacNextLoop = _timing.CurTime + EvacReissue;
        Dirty(ent);

        StartAlarm(ent);
        Announce(ent.Owner, announceKey, ("seconds", seconds));
        PopupOnGrid(ent.Owner, "wf-chunk-evac-popup");
    }

    /// <summary>Stops the chunk's alarm; idempotent, because it is called at landing and again at cleanup.</summary>
    public void StopEvacuation(Entity<WFPlanetChunkComponent> ent)
    {
        ent.Comp.Evacuating = false;
        ent.Comp.EvacStream = _audio.Stop(ent.Comp.EvacStream);
        _evacBeats.Remove(ent.Owner);
        Dirty(ent);
    }

    /// <summary>The pre-drop 30 s and 10 s beats, worded for the people standing on the chunk rather than the hull.</summary>
    private void UpdateEvacuating(Entity<WFPlanetChunkComponent> ent)
    {
        if (!ent.Comp.Evacuating)
            return;

        // The 60 s pre-drop window is the one the re-issue was written for: without this the alarm would play once on
        // the Disconnecting edge and not again until the chunk was already falling, which is where UpdateDropped picks
        // the cadence back up.
        ReissueAlarm(ent);

        if (ent.Comp.Cracker is not { } net
            || !TryGetEntity(net, out var cracker)
            || !TryComp<WFPlanetCrackerComponent>(cracker, out var comp)
            || !comp.EvacRunning)
        {
            return;
        }

        var remaining = comp.EvacEnd - _timing.CurTime;
        var beat = _evacBeats.GetValueOrDefault(ent.Owner);
        var fired = beat;

        if (beat < 1 && remaining <= TimeSpan.FromSeconds(30))
        {
            PopupOnGrid(ent.Owner, "wf-chunk-evac-warning", ("seconds", 30));
            fired = 1;
        }

        if (fired < 2 && remaining <= TimeSpan.FromSeconds(10))
        {
            PopupOnGrid(ent.Owner, "wf-chunk-evac-warning", ("seconds", 10));
            fired = 2;
        }

        if (fired != beat)
            _evacBeats[ent.Owner] = fired;
    }

    /// <summary>
    /// The sweep branch for a chunk that has already been pushed into transit: re-issue the alarm, watch for the
    /// landing and arm the cleanup. It runs for EVERY dropped chunk, the F4 hull-fall drop and the D24 watchdog drop
    /// included: a chunk that has crashed into a planet is a wreck whichever path dropped it, and a per-path flag would
    /// leave two of the three littering permanently cleanup-immune grids on the ground layer for the rest of the round.
    /// </summary>
    private void UpdateDropped(Entity<WFPlanetChunkComponent> ent)
    {
        // Defensive handling for an inconsistent/admin-edited dropped state: without transit admission this is not
        // evidence of a landing and must never trigger cleanup in the berth. Normal failed attempts now leave
        // Dropped false and retain ownership so release can retry.
        if (!ent.Comp.EnteredTransit)
        {
            if (ent.Comp.Evacuating || ent.Comp.DropStream is not null)
            {
                ent.Comp.DropStream = _audio.Stop(ent.Comp.DropStream);
                StopEvacuation(ent);
            }

            return;
        }

        ReissueAlarm(ent);

        if (!ent.Comp.Landed)
        {
            // The UpdateFall idiom (WFCrackerSystem.Crack.cs:473-482): the CE touchdown raises no event this system
            // subscribes, but the transit map is deleted the moment the convoy lands.
            if (Transform(ent.Owner).MapUid is { } map && HasComp<CEZTransitMapComponent>(map))
                return;

            OnLanded(ent);
            return;
        }

        if (_timing.CurTime >= ent.Comp.LandedAt + ent.Comp.CleanupDelay)
            _cleanupBuffer.Add(ent);
    }

    /// <summary>The chunk is down: cut both loops, re-assert the pose, freeze the wreck and re-open the crater.</summary>
    private void OnLanded(Entity<WFPlanetChunkComponent> ent)
    {
        // Nothing else stops the falling-rock loop: the sweep used to skip a dropped chunk forever.
        ent.Comp.DropStream = _audio.Stop(ent.Comp.DropStream);

        // The only reliable stop for the chunk alarm on the normal path; the state-change escape branch is already
        // blind by now because the hull's back-link was nulled inside DropChunk.
        StopEvacuation(ent);

        ent.Comp.Landed = true;
        ent.Comp.LandedAt = _timing.CurTime;
        Dirty(ent);

        // The chunk is a DYNAMIC body for the whole fall - TryEnterTransit's own Enable is un-forced and nothing
        // re-disables it - so ground friction and wall collision would otherwise slide it off its own crater.
        _transform.SetWorldPositionRotation(ent.Owner, ent.Comp.DropWorldPos, ent.Comp.DropWorldRot);

        // Static also stops the CE fall sweep ever looking at the wreck again: it skips static bodies.
        _shuttle.Disable(ent.Owner, force: true);
        EnsureComp<PreventGridAnchorChangesComponent>(ent.Owner);

        var ground = EntityUid.Invalid;

        if (ent.Comp.GroundMap is { } netGround && TryGetEntity(netGround, out var groundUid))
        {
            ground = groundUid.Value;
            _scars.ReStamp(ground);
        }

        var cracker = ent.Comp.Cracker is { } netCracker && TryGetEntity(netCracker, out var crackerUid)
            ? crackerUid.Value
            : EntityUid.Invalid;

        var ev = new WFChunkLandedEvent(ent.Owner, cracker, ground);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>
    /// Removes the wreck once the crash explosions have had time to drain. Called ONLY from the post-enumeration
    /// cleanup drain: UnparentPlayersFromGrid deletes synchronously.
    /// A bare QueueDel is not an option - it recursively deletes every rider with no body and no admin log - and
    /// nothing else will ever remove the grid, because ParkChunk gave it CleanupImmuneComponent (Extraction.cs:433).
    /// </summary>
    private void Cleanup(Entity<WFPlanetChunkComponent> ent)
    {
        StopEvacuation(ent);

        var ground = ent.Comp.GroundMap is { } net && TryGetEntity(net, out var uid) ? uid : null;

        _lifecycle.UnparentPlayersFromGrid(ent.Owner, deleteGrid: true);

        // Driven off the ground-grid scar, so it still works now the chunk component is gone.
        if (ground is { } groundMap)
            _scars.ReStamp(groundMap);
    }

    /// <summary>
    /// Replays the alarm loop once its cadence is up, for both halves of the evacuation: PlayGlobal freezes its
    /// recipient set at play time, so anyone boarding between two issues would otherwise hear nothing at all.
    /// </summary>
    private void ReissueAlarm(Entity<WFPlanetChunkComponent> ent)
    {
        if (!ent.Comp.Evacuating || _timing.CurTime < ent.Comp.EvacNextLoop)
            return;

        StartAlarm(ent);
        ent.Comp.EvacNextLoop = _timing.CurTime + EvacReissue;
    }

    /// <summary>(Re)starts the looping alarm on everyone currently aboard the chunk.</summary>
    private void StartAlarm(Entity<WFPlanetChunkComponent> ent)
    {
        ent.Comp.EvacStream = _audio.Stop(ent.Comp.EvacStream);

        var filter = Filter.Empty().AddInGrid(ent.Owner, EntityManager);

        ent.Comp.EvacStream = _audio
            .PlayGlobal(ent.Comp.EvacSound, filter, true, AudioParams.Default.WithLoop(true).WithVolume(-4f))?.Entity;
    }

    /// <summary>
    /// One announcement to everyone aboard one grid. It takes arguments because DispatchFilteredAnnouncement wants a
    /// PRE-FORMATTED string (ChatSystem.cs:388-395) and most of these keys carry a { $seconds } variable.
    /// </summary>
    private void Announce(EntityUid grid, string key, params (string, object)[] args)
    {
        var filter = Filter.Empty().AddInGrid(grid, EntityManager);

        _chat.DispatchFilteredAnnouncement(
            filter,
            Loc.GetString(key, args),
            sender: Loc.GetString("wf-crack-announce-sender"),
            playSound: false,
            colorOverride: Color.Red);
    }

    /// <summary>
    /// One popup per person aboard one grid, anchored on that person's OWN entity: the client silently drops a popup
    /// whose anchor is outside its PVS.
    /// </summary>
    private void PopupOnGrid(EntityUid grid, string key, params (string, object)[] args)
    {
        var message = Loc.GetString(key, args);
        var filter = Filter.Empty().AddInGrid(grid, EntityManager);

        foreach (var player in filter.Recipients)
        {
            if (player.AttachedEntity is not { } uid)
                continue;

            _popup.PopupEntity(message, uid, uid, PopupType.LargeCaution);
        }
    }
}
