using Content.Server._CE.ZLevels.Core.Components;
using Content.Server.Gravity;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Gravity;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>The crack and grace timers, the abort spin-down and the fall, driven by one sweep.</summary>
public sealed partial class WFCrackerSystem
{
    /// <summary>Hull rumbles between ambience re-cuts: five is once a minute.</summary>
    private const int AmbienceRecutShakes = 5;

    private readonly Dictionary<EntityUid, int> _recutCounter = new();

    /// <summary>How often the hull shake re-triggers while cutting; the engine shake lasts about 2 s.</summary>
    private static readonly TimeSpan ShakeInterval = TimeSpan.FromSeconds(12);

    private TimeSpan _nextSweep;

    /// <summary>Next shake per cutting hull.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextShake = new();

    /// <summary>Crackers to sweep, collected first so the fall may create maps and move grids mid-pass.</summary>
    private readonly List<Entity<WFPlanetCrackerComponent>> _sweepBuffer = new();

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _sweepBuffer.Clear();

        var fast = false;
        var query = EntityQueryEnumerator<WFPlanetCrackerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            fast |= comp.State is WFCrackState.Cracking or WFCrackState.Disconnecting || comp.DisconnectArmed;
            _sweepBuffer.Add((uid, comp));
        }

        // A hull deleted mid-cut never gets another pass to clear its own shake entry, so prune by count.
        if (_nextShake.Count > _sweepBuffer.Count)
        {
            _nextShake.Clear();
        }

        foreach (var cracker in _sweepBuffer)
        {
            SweepCracker(cracker);
        }

        // 1 Hz normally, 0.25 s while any hull is cutting so the console timers read live.
        _nextSweep = _timing.CurTime + TimeSpan.FromSeconds(fast ? 0.25 : 1.0);
    }

    /// <summary>One hull's pass: the survey edges, then the timers, then everything the crew looks at.</summary>
    private void SweepCracker(Entity<WFPlanetCrackerComponent> ent)
    {
        // Refreshed every sweep, as the marker may be rotated or re-wrenched after map init.
        UpdateBerthPose(ent);

        // Before anything that reads the hull's pose: a locked hull that somehow came loose is pinned back first.
        ReassertLock(ent);

        UpdateSurvey(ent);

        // Reconciled against the anchors too, so a missed edge can't leave a hull claiming a lost pair.
        if (ent.Comp.State is WFCrackState.Surveying or WFCrackState.AnchorsPlaced or WFCrackState.AnchorsLocked)
            ReconcilePair(ent);

        // Ahead of the chain: an armed pairing window must run down from any state.
        UpdateDisconnectWindow(ent);

        // Likewise, a hull pulled out of Disconnecting by anything else needs its alarm cut here.
        UpdateStrandedEvacuation(ent);

        if (ent.Comp.PendingAbort is not null)
        {
            // The grace timer is suspended for the whole spin-down: the crew is not being asked to hold anything.
            if (_timing.CurTime >= ent.Comp.AbortEnd)
                FinishAbort(ent);
        }
        else if (ent.Comp.State is WFCrackState.Cracking or WFCrackState.Cracked)
        {
            // A pair can dissolve with no broken or destroyed event, so the sweep catches it.
            if (!TryGetTargetedPair(ent, out _, out _))
            {
                StartAbort(ent, WFCrackState.AnchorsPlaced);
            }
            else
            {
                UpdateCrackPause(ent);
                UpdateCrackTimer(ent);
                UpdateGrace(ent);
            }
        }
        else if (ent.Comp.State == WFCrackState.Falling)
        {
            UpdateFall(ent);
        }
        // Safe after the PendingAbort arm: EnterDisconnecting clears it and OnSwitchedOff refuses while it is set.
        else if (ent.Comp.State == WFCrackState.Disconnecting)
        {
            UpdateEvacuation(ent);
        }
        else if (ent.Comp.State == WFCrackState.Released)
        {
            UpdateRelease(ent);
        }

        ent.Comp.Blockers = ComputeBlockers(ent);

        ReconcileProjectors(ent);
        UpdateShake(ent);
        ReconcileBeams(ent);
        UpdateSiteEffects(ent);
    }

    /// <summary>Arms the crack timer from the pair distance and the hull's part multiplier, and starts the rumble.</summary>
    private void StartCrack(
        Entity<WFPlanetCrackerComponent> ent,
        Entity<WFGravityAnchorComponent> a,
        Entity<WFGravityAnchorComponent> b)
    {
        // The anchor system's own distance, so the crack time and the anchor readouts agree.
        if (!_anchors.TryGetPairDistance(a, out var distance))
            distance = (TransformSystem.GetWorldPosition(a.Owner) - TransformSystem.GetWorldPosition(b.Owner)).Length();

        var duration = GetCrackDuration(
            distance,
            GetPartMultiplier(ent.Owner),
            ent.Comp.BaseCrackTime,
            ent.Comp.ReferenceDistance);

        ent.Comp.CrackDuration = duration;
        ent.Comp.CrackRemaining = duration;
        ent.Comp.CrackEnd = _timing.CurTime + duration;
        ent.Comp.CrackPaused = false;
        ent.Comp.PendingAbort = null;
        ent.Comp.AbortEnd = TimeSpan.Zero;
        ent.Comp.Failing = WFCrackFailure.None;
        ent.Comp.GraceRunning = false;
        ent.Comp.GraceEnd = TimeSpan.Zero;
        Dirty(ent);

        SetState(ent, WFCrackState.Cracking);

        ReconcileProjectors(ent);

        // The ring starts empty and grows.
        _anchors.SetCrackProgress(a, 0f);
        _anchors.SetCrackProgress(b, 0f);

        StartShipAmbience(ent);
        StartGroundRumble(ent, a, b);
        StartBeams(ent, a, b);
    }

    /// <summary>The ship-side ambience, to everyone aboard the hull rather than a point source at its origin.</summary>
    private void StartShipAmbience(Entity<WFPlanetCrackerComponent> ent)
    {
        StopRumble(ent);
        ent.Comp.RumbleStream = _audio.PlayGlobal(
            ent.Comp.RumbleSound,
            _audience.Aboard(ent.Owner),
            true,
            AudioParams.Default.WithLoop(true))?.Entity;
    }

    /// <summary>The beams igniting and then holding: once and then looped, at every projector and both anchors.</summary>
    private void StartBeams(
        Entity<WFPlanetCrackerComponent> ent,
        Entity<WFGravityAnchorComponent> a,
        Entity<WFGravityAnchorComponent> b)
    {
        StopBeams(ent);
        GetProjectors(ent.Owner, _projectorBuffer);

        foreach (var projector in _projectorBuffer)
        {
            StartBeam(ent, projector.Owner);
        }

        StartBeam(ent, a.Owner);
        StartBeam(ent, b.Owner);
    }

    private void StartBeam(Entity<WFPlanetCrackerComponent> ent, EntityUid at)
    {
        _audio.PlayPvs(ent.Comp.BeamFireSound, at);

        if (_audio.PlayPvs(ent.Comp.BeamLoopSound, at, AudioParams.Default.WithLoop(true)) is { } loop)
            ent.Comp.BeamStreams.Add(loop.Entity);
    }

    /// <summary>Cuts every beam loop.</summary>
    private void StopBeams(Entity<WFPlanetCrackerComponent> ent)
    {
        foreach (var stream in ent.Comp.BeamStreams)
        {
            _audio.Stop(stream);
        }

        ent.Comp.BeamStreams.Clear();
    }

    /// <summary>One crack of the ground at the cut circle, for everyone near it.</summary>
    private void PlayGroundCrackEffect(Entity<WFPlanetCrackerComponent> ent)
    {
        if (!TryGetTargetedPair(ent, out var a, out var b) || !TryGetCircle(a.Owner, b.Owner, out var centre, out _))
            return;

        if (Transform(a.Owner).MapUid is not { } groundMap)
            return;

        _audio.PlayPvs(ent.Comp.CrackEffectSound, new EntityCoordinates(groundMap, centre));
    }

    /// <summary>The mean of every healthy projector's crack-time multiplier, so one upgrade still counts.</summary>
    public float GetPartMultiplier(EntityUid cracker)
    {
        GetProjectors(cracker, _projectorBuffer);

        var total = 0f;
        var count = 0;

        foreach (var projector in _projectorBuffer)
        {
            if (projector.Comp.Broken || !_receiver.IsPowered(projector.Owner))
                continue;

            total += projector.Comp.CrackTimeMultiplier;
            count++;
        }

        return count == 0 ? 1f : total / count;
    }

    /// <summary>Holds the cut while a targeted anchor is damaged, banking the remainder, not the deadline.</summary>
    public void UpdateCrackPause(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.State != WFCrackState.Cracking || ent.Comp.PendingAbort is not null)
            return;

        var damaged = TryGetTargetedPair(ent, out var a, out var b) && (a.Comp.Damaged || b.Comp.Damaged);

        if (damaged == ent.Comp.CrackPaused)
            return;

        if (damaged)
        {
            var remaining = ent.Comp.CrackEnd - _timing.CurTime;
            ent.Comp.CrackRemaining = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
        else
        {
            ent.Comp.CrackEnd = _timing.CurTime + ent.Comp.CrackRemaining;
        }

        ent.Comp.CrackPaused = damaged;
        Dirty(ent);
    }

    /// <summary>Counts the cut down and completes it; the remainder is kept live for the console readout.</summary>
    private void UpdateCrackTimer(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.State != WFCrackState.Cracking || ent.Comp.CrackPaused)
            return;

        if (_timing.CurTime < ent.Comp.CrackEnd)
        {
            ent.Comp.CrackRemaining = ent.Comp.CrackEnd - _timing.CurTime;

            // The ring grows off the same remainder the console reads, so the two can never disagree.
            if (ent.Comp.CrackDuration > TimeSpan.Zero && TryGetTargetedPair(ent, out var a, out var b))
            {
                var progress = 1f - (float)(ent.Comp.CrackRemaining / ent.Comp.CrackDuration);

                _anchors.SetCrackProgress(a, progress);
                _anchors.SetCrackProgress(b, progress);
            }

            return;
        }

        CompleteCrack(ent);
    }

    /// <summary>The chunk is cut free: raises the extraction hook and stops the rumble.</summary>
    public void CompleteCrack(Entity<WFPlanetCrackerComponent> ent)
    {
        if (!TryGetTargetedPair(ent, out var a, out var b))
            return;

        TryGetCircle(a, b, out var centre, out var radius);
        var groundMap = Transform(a.Owner).MapUid ?? EntityUid.Invalid;

        ent.Comp.CrackRemaining = TimeSpan.Zero;
        ent.Comp.CrackPaused = false;
        Dirty(ent);

        SetState(ent, WFCrackState.Cracked);
        StopRumble(ent);
        StopGroundRumble(ent);

        // A finished cut is a full circle, exactly: the endpoints of the progress value always land.
        _anchors.SetCrackProgress(a, 1f);
        _anchors.SetCrackProgress(b, 1f);

        var ev = new WFCrackCompletedEvent(ent.Owner, a.Owner, b.Owner, centre, radius, groundMap);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>What the hull is failing, read from the machines, never the display-only projector state.</summary>
    public WFCrackFailure ComputeFailures(Entity<WFPlanetCrackerComponent> ent)
    {
        var failing = WFCrackFailure.None;

        if (!TryGetCentrifuge(ent.Owner, out var centrifuge) || !centrifuge.Comp.AtFull)
            failing |= WFCrackFailure.Centrifuge;

        GetProjectors(ent.Owner, _projectorBuffer);

        if (_projectorBuffer.Count < ent.Comp.RequiredProjectors)
            failing |= WFCrackFailure.ProjectorsShort;

        foreach (var projector in _projectorBuffer)
        {
            if (projector.Comp.Broken)
                failing |= WFCrackFailure.ProjectorBroken;

            if (!_receiver.IsPowered(projector.Owner))
                failing |= WFCrackFailure.ProjectorPower;
        }

        return failing;
    }

    /// <summary>The grace countdown: armed while anything is failing, disarmed when it is fixed, and the fall on expiry.</summary>
    private void UpdateGrace(Entity<WFPlanetCrackerComponent> ent)
    {
        ent.Comp.Failing = ComputeFailures(ent);

        var failing = ent.Comp.Failing != WFCrackFailure.None;

        if (failing != ent.Comp.GraceRunning)
        {
            ent.Comp.GraceRunning = failing;

            if (failing)
            {
                ent.Comp.GraceEnd = _timing.CurTime + ent.Comp.GraceDuration;
                ent.Comp.KlaxonStream = _audio.PlayPvs(ent.Comp.KlaxonSound, ent.Owner, AudioParams.Default.WithLoop(true))?.Entity;
            }
            else
            {
                StopKlaxon(ent);
            }

            Dirty(ent);
        }

        if (ent.Comp.GraceRunning && _timing.CurTime >= ent.Comp.GraceEnd)
            Fall(ent);
    }

    /// <summary>Starts the spin-down after a targeted anchor is lost; the hull keeps its lock until it runs out.</summary>
    public void StartAbort(Entity<WFPlanetCrackerComponent> ent, WFCrackState pending)
    {
        if (ent.Comp.State is not (WFCrackState.Cracking or WFCrackState.Cracked))
            return;

        // The first loss wins: a second one must not extend the spin-down or change where it lands.
        if (ent.Comp.PendingAbort is not null)
            return;

        ent.Comp.PendingAbort = pending;
        ent.Comp.AbortEnd = _timing.CurTime + ent.Comp.AbortSpinDown;
        ent.Comp.GraceRunning = false;
        ent.Comp.Failing = WFCrackFailure.None;
        Dirty(ent);

        StopKlaxon(ent);
        StopRumble(ent);
    }

    /// <summary>Ends the spin-down: the lock comes off, every timer is cleared and the hull drops back a stage.</summary>
    private void FinishAbort(Entity<WFPlanetCrackerComponent> ent)
    {
        var pending = ent.Comp.PendingAbort ?? WFCrackState.Surveying;

        // Before ClearTarget, which is the last moment the anchors are still findable through the hull.
        ResetCrackProgress(ent);

        ReleaseLock(ent);
        ClearTarget(ent);

        ent.Comp.CrackEnd = TimeSpan.Zero;
        ent.Comp.CrackRemaining = TimeSpan.Zero;
        ent.Comp.CrackDuration = TimeSpan.Zero;
        ent.Comp.CrackPaused = false;
        ent.Comp.GraceEnd = TimeSpan.Zero;
        ent.Comp.GraceRunning = false;
        ent.Comp.Failing = WFCrackFailure.None;
        ent.Comp.AbortEnd = TimeSpan.Zero;
        ent.Comp.PendingAbort = null;
        Dirty(ent);

        StopRumble(ent);
        StopGroundRumble(ent);
        StopKlaxon(ent);

        SetState(ent, pending);
    }

    /// <summary>Drops the hull down the planet's z-stack; the order matters.</summary>
    public void Fall(Entity<WFPlanetCrackerComponent> ent)
    {
        // Release first, or TryEnterTransit leaves the grid static for the whole fall.
        ReleaseLock(ent);

        // Stop the centrifuge counting as lift; its active flag is the only lever that works at once.
        var query = EntityQueryEnumerator<WFCentrifugeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != ent.Owner || !TryComp<GravityGeneratorComponent>(uid, out var gravgen))
                continue;

            _gravgen.WfSetGravityActive((uid, gravgen), false);
        }

        // Rebuild the lift cache next tick and outrun the exit band, or the hull pops back onto orbit.
        _zLevels.WfInvalidateGravgenCapacity();

        var faller = EnsureComp<CEZGridFallerComponent>(ent.Owner);
        faller.Velocity = SharedWFCrackerSystem.FallSeedVelocity;
        faller.GravityTime = _timing.CurTime;

        if (!TryComp<MapGridComponent>(ent.Owner, out var grid))
        {
            Log.Error($"{ToPrettyString(ent.Owner)} tried to fall but is not a grid.");
        }
        else if (!_zLevels.TryEnterTransit((ent.Owner, grid), 1.0f))
        {
            // It returns false without logging when the map is not a z-map or is already a transit map.
            Log.Error($"{ToPrettyString(ent.Owner)} could not be pushed into transit for its crack fall.");
        }

        // Coming down with no lift is what the flight alarms are for.
        if (_zLevels.WfTryGetLiftRatio(ent.Owner, out var liftRatio))
            _flight.EnterLiftLost(ent.Owner, liftRatio);

        SetState(ent, WFCrackState.Falling);

        // The chunk joins the fall.
        var ev = new WFCrackerFallingEvent(ent.Owner);
        RaiseLocalEvent(ref ev);

        StopRumble(ent);
        StopGroundRumble(ent);
        StopKlaxon(ent);
        ent.Comp.GraceRunning = false;
        ent.Comp.FallStream = _audio.PlayPvs(ent.Comp.FallSound, ent.Owner, AudioParams.Default.WithLoop(true))?.Entity;
        Dirty(ent);
    }

    /// <summary>Reconciles projector state each sweep; the projector system overwrites it on power or repair.</summary>
    private void ReconcileProjectors(Entity<WFPlanetCrackerComponent> ent)
    {
        var cutting = ent.Comp.State == WFCrackState.Cracking && ent.Comp.PendingAbort is null;

        GetProjectors(ent.Owner, _projectorBuffer);

        foreach (var projector in _projectorBuffer)
        {
            WFProjectorState desired;

            if (projector.Comp.Broken)
                desired = WFProjectorState.Broken;
            else if (!_receiver.IsPowered(projector.Owner))
                desired = WFProjectorState.Off;
            else if (cutting)
                desired = ent.Comp.CrackPaused ? WFProjectorState.Charging : WFProjectorState.Firing;
            else
                desired = WFProjectorState.Idle;

            _projectors.SetState(projector, desired);
        }
    }

    /// <summary>Re-triggers the hull shake while the cut runs; the engine shake has no duration or intensity knob.</summary>
    private void UpdateShake(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.State != WFCrackState.Cracking || ent.Comp.PendingAbort is not null)
        {
            _nextShake.Remove(ent.Owner);
            return;
        }

        if (_nextShake.TryGetValue(ent.Owner, out var next) && _timing.CurTime < next)
            return;

        _nextShake[ent.Owner] = _timing.CurTime + ShakeInterval;

        if (TryComp<GravityComponent>(ent.Owner, out var gravity))
            _gravity.StartGridShake(ent.Owner, gravity);

        PlayGroundCrackEffect(ent);

        // The ambience loops only reach whoever was there at the start; re-cut now and then for newcomers.
        var recuts = _recutCounter.GetValueOrDefault(ent.Owner) + 1;
        _recutCounter[ent.Owner] = recuts;

        if (recuts % AmbienceRecutShakes == 0 && TryGetTargetedPair(ent, out var a, out var b))
        {
            StartShipAmbience(ent);
            StartGroundRumble(ent, a, b);
        }
    }

    /// <summary>Cuts the fall alarm once the hull is down, detected by its transit map being deleted.</summary>
    private void UpdateFall(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.FallStream is null)
            return;

        if (Transform(ent.Owner).MapUid is { } map && HasComp<CEZTransitMapComponent>(map))
            return;

        StopFall(ent);
    }

    /// <summary>Stops the cut rumble loop.</summary>
    private void StopRumble(Entity<WFPlanetCrackerComponent> ent)
    {
        ent.Comp.RumbleStream = _audio.Stop(ent.Comp.RumbleStream);
        StopBeams(ent);
    }

    /// <summary>Starts the ground-side rumble; the hull's loop is silent across maps.</summary>
    private void StartGroundRumble(
        Entity<WFPlanetCrackerComponent> ent,
        Entity<WFGravityAnchorComponent> a,
        Entity<WFGravityAnchorComponent> b)
    {
        StopGroundRumble(ent);

        if (!TryGetCircle(a.Owner, b.Owner, out var centre, out _))
            return;

        var xform = Transform(a.Owner);

        if (xform.MapUid is not { } groundMap)
            return;

        // The whole planet hears the cut, not a radius round the circle: two loops to everyone on the ground layer.
        var audience = Filter.Empty().AddInMap(xform.MapID, EntityManager);

        ent.Comp.GroundRumbleStream = _audio.PlayGlobal(
            ent.Comp.GroundRumbleSound, audience, true, AudioParams.Default.WithLoop(true))?.Entity;
        ent.Comp.GroundAmbienceStream2 = _audio.PlayGlobal(
            ent.Comp.GroundAmbienceSound2, audience, true, AudioParams.Default.WithLoop(true))?.Entity;
    }

    /// <summary>Stops the ground-side rumble loop.</summary>
    private void StopGroundRumble(Entity<WFPlanetCrackerComponent> ent)
    {
        ent.Comp.GroundRumbleStream = _audio.Stop(ent.Comp.GroundRumbleStream);
        ent.Comp.GroundAmbienceStream2 = _audio.Stop(ent.Comp.GroundAmbienceStream2);
    }

    /// <summary>Resets the targeted anchors' rings to full, each resolved alone since one may be gone.</summary>
    private void ResetCrackProgress(Entity<WFPlanetCrackerComponent> ent)
    {
        if (TryGetAnchor(ent.Comp.AnchorA, out var a))
            _anchors.SetCrackProgress(a, 1f);

        if (TryGetAnchor(ent.Comp.AnchorB, out var b))
            _anchors.SetCrackProgress(b, 1f);
    }

    /// <summary>Stops the fall alarm loop.</summary>
    private void StopFall(Entity<WFPlanetCrackerComponent> ent)
    {
        ent.Comp.FallStream = _audio.Stop(ent.Comp.FallStream);
    }

    /// <summary>Stops the grace klaxon loop.</summary>
    private void StopKlaxon(Entity<WFPlanetCrackerComponent> ent)
    {
        ent.Comp.KlaxonStream = _audio.Stop(ent.Comp.KlaxonStream);
    }
}
