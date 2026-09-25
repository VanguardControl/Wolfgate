using Content.Server._NF.Shuttles.Components;
using Content.Server.Chat.Systems;
using Content.Server.StationEvents.Events;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.PlanetCracker.Chunk;

/// <summary>The chunk's half of the disconnect protocol: evacuation alarm, landing and wreck cleanup.</summary>
public sealed partial class WFPlanetChunkSystem
{
    /// <summary>Alarm loop replay interval; PlayGlobal freezes its recipients at play time.</summary>
    private static readonly TimeSpan EvacReissue = TimeSpan.FromSeconds(15);

    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private LinkedLifecycleGridSystem _lifecycle = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedGridTraversalSystem _traversal = default!;
    [Dependency] private WFCrackScarSystem _scars = default!;

    /// <summary>Landed chunks to clean up after the sweep, since the cleanup deletes synchronously.</summary>
    private readonly List<Entity<WFPlanetChunkComponent>> _cleanupBuffer = new();

    /// <summary>Map-level entities under a chunk that has just been snapped back onto its crater.</summary>
    private readonly HashSet<EntityUid> _snapCovered = new();

    /// <summary>Pre-drop beats each evacuating chunk has fired, so a re-entered sweep can't repeat one.</summary>
    private readonly Dictionary<EntityUid, byte> _evacBeats = new();

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

        // An admin pulled the hull out of Disconnecting pre-drop; matched by owner, as DropChunk nulls the link.
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

    /// <summary>Drops the chunk once the evacuation runs out.</summary>
    private void OnCrackerReleasing(ref WFCrackerReleasingEvent args)
    {
        if (!TryComp<WFPlanetCrackerComponent>(args.Cracker, out var cracker))
            return;

        if (!TryGetChunk((args.Cracker, cracker), out var chunk))
            return;

        DropChunk(chunk);
    }

    /// <summary>Starts the chunk's own alarm, announcement and popup.</summary>
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

        // Without this the alarm would play once and not again until the chunk falls.
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

    /// <summary>Sweep branch for any dropped chunk: re-issue the alarm, detect the landing, arm the cleanup.</summary>
    private void UpdateDropped(Entity<WFPlanetChunkComponent> ent)
    {
        // Without transit admission this is no landing, so never clean up in the berth.
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
            // CE touchdown raises no event here, but the transit map is deleted the moment the convoy lands.
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
        // Nothing else stops the falling-rock loop.
        ent.Comp.DropStream = _audio.Stop(ent.Comp.DropStream);

        // The only stop for the chunk alarm on the normal path.
        StopEvacuation(ent);

        ent.Comp.Landed = true;
        ent.Comp.LandedAt = _timing.CurTime;
        Dirty(ent);

        // The chunk is dynamic through the fall, so it may have slid off its crater.
        _transform.SetWorldPositionRotation(ent.Owner, ent.Comp.DropWorldPos, ent.Comp.DropWorldRot);
        TraverseSnapCovered(ent.Owner);

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

    /// <summary>Grid-traverses whatever the landing snap put the chunk over, before the physics step can.</summary>
    // In FindGridContacts the traversal of a mob's first proxy destroys all its proxies and the loop re-adds the
    // rest; the chunk frame equals the ground's, so that stale proxy repeats the live one's pairs and AddPair asserts.
    private void TraverseSnapCovered(EntityUid chunk)
    {
        if (!_traversal.Enabled
            || Transform(chunk).MapUid is not { } map
            || !TryComp<MapGridComponent>(chunk, out var grid))
            return;

        // A map sits at the world origin, so the chunk's world AABB is also the map-local one.
        var aabb = _transform.GetWorldMatrix(chunk).TransformBox(grid.LocalAABB);

        _snapCovered.Clear();
        _lookup.GetLocalEntitiesIntersecting(map, aabb, _snapCovered,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sensors | LookupFlags.Approximate);

        foreach (var uid in _snapCovered)
        {
            var xform = Transform(uid);

            // The engine traversal's own filter: loose, map-parented, not a grid.
            if (xform.ParentUid != map || xform.Anchored || !xform.GridTraversal || HasComp<MapGridComponent>(uid))
                continue;

            _traversal.CheckTraversal(uid, xform, map);
        }
    }

    /// <summary>Removes the wreck after the blasts drain; call after the sweep, as it deletes synchronously.</summary>
    private void Cleanup(Entity<WFPlanetChunkComponent> ent)
    {
        StopEvacuation(ent);

        var ground = ent.Comp.GroundMap is { } net && TryGetEntity(net, out var uid) ? uid : null;

        // Not a bare QueueDel, which would delete every rider with it.
        _lifecycle.UnparentPlayersFromGrid(ent.Owner, deleteGrid: true);

        // Driven off the ground-grid scar, so it still works now the chunk component is gone.
        if (ground is { } groundMap)
            _scars.ReStamp(groundMap);
    }

    /// <summary>Replays the alarm loop once its cadence is up.</summary>
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

    /// <summary>One announcement to everyone aboard one grid.</summary>
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

    /// <summary>One popup per person aboard a grid, anchored on each person so none is dropped outside PVS.</summary>
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
