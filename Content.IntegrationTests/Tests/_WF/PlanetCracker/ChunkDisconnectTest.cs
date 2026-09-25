#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.PlanetCracker.Anchors;
using Content.Server._WF.PlanetCracker.Chunk;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server.Decals;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Fissures;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>The disconnect protocol: veto, pairing window, evacuation, release, landing and cleanup.</summary>
[TestFixture]
[TestOf(typeof(WFCrackerSystem))]
public sealed class ChunkDisconnectTest
{
    /// <summary>Long enough that no test's wreck is deleted out from under its own landing assertions.</summary>
    private static readonly TimeSpan NoCleanup = TimeSpan.FromSeconds(60);

    /// <summary>Short enough that a cleanup is reachable inside one test's wait.</summary>
    private static readonly TimeSpan FastCleanup = TimeSpan.FromSeconds(1);

    // ---------------------------------------------------------------------------------------------------- VETO

    /// <summary>A cut that is still running is not a disconnect; the anchors stay on until the disc is free.</summary>
    [Test]
    public async Task TheVetoRefusesASwitchOffBeforeCracked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.Cracking), "Precondition: the hull is still cutting.");

            var attempt = RaiseAttempt(entMan, site.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(attempt.Cancelled, Is.True, "A switch-off was allowed while the cut was still running.");
                Assert.That(attempt.Reason, Is.Not.Null, "The refusal carried no reason for the popup to show.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>An anchor lost mid-cut spins the projectors down, and the crew may not disconnect then.</summary>
    [Test]
    public async Task TheVetoRefusesASwitchOffDuringAnAbortSpinDown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();

        var site = await BuildExtracted(pair);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracked), "Precondition: the disc is cut free.");

            // Arms the spin-down without breaking the pair.
            crackers.StartAbort((site.Cracker, comp), WFCrackState.AnchorsPlaced);

            Assert.That(comp.PendingAbort, Is.Not.Null, "Precondition: a spin-down is pending.");

            var attempt = RaiseAttempt(entMan, site.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(attempt.Cancelled, Is.True, "A switch-off was allowed during an abort spin-down.");
                Assert.That(attempt.Reason, Is.Not.Null, "The refusal carried no reason for the popup to show.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>An anchor still on the surface after the cut is not part of it, so it is vetoed.</summary>
    [Test]
    public async Task TheVetoRefusesAnAnchorStillOnTheSurface()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildReadyToCut(pair);

        // One callback: a sweep in between would arm a spin-down and refuse for a different reason.
        await server.WaitAssertion(() =>
        {
            server.ConsoleHost.ExecuteCommand(null, "wfcracker state Cracked");

            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracked), "Precondition: the hull was forced to Cracked.");
                Assert.That(comp.PendingAbort, Is.Null, "Precondition: nothing is spinning down.");
                Assert.That(entMan.GetComponent<TransformComponent>(site.Anchors[0]).GridUid, Is.EqualTo(site.Ground),
                    "Precondition: the anchor is still on the ground layer.");
            }

            var attempt = RaiseAttempt(entMan, site.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(attempt.Cancelled, Is.True, "A ground anchor was allowed to start a disconnect.");
                Assert.That(attempt.Reason, Is.EqualTo(Localise(pair, "wf-anchor-off-not-on-chunk")),
                    "The refusal named the wrong reason.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The one case the protocol accepts: a cut hull and an anchor riding the disc it cut.</summary>
    [Test]
    public async Task TheVetoAllowsASwitchOffOnTheChunk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildExtracted(pair);

        await server.WaitAssertion(() =>
        {
            var chunk = FindChunk(entMan);

            Assert.That(entMan.GetComponent<TransformComponent>(site.Anchors[0]).GridUid, Is.EqualTo(chunk),
                "Precondition: the anchor rode the disc up.");

            var attempt = RaiseAttempt(entMan, site.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(attempt.Cancelled, Is.False, "The one switch-off the protocol exists for was refused.");
                Assert.That(attempt.Reason, Is.Null, "An allowed attempt still carried a refusal reason.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>An unowned anchor passes the veto uncancelled rather than refusing on a null owner.</summary>
    [Test]
    public async Task AnUnownedAnchorIsNeverVetoed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildExtracted(pair);
        var loose = EntityUid.Invalid;

        await server.WaitPost(() =>
            loose = entMan.SpawnEntity(AnchorProto, new EntityCoordinates(site.Ground, new Vector2(-12.5f, -12.5f))));

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(loose).Cracker, Is.Null,
                "Precondition: the hand-spawned anchor belongs to nobody.");

            var cracked = RaiseAttempt(entMan, loose);

            // Again from a stage where an owned anchor would be refused.
            server.ConsoleHost.ExecuteCommand(null, "wfcracker state Surveying");

            var surveying = RaiseAttempt(entMan, loose);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cracked.Cancelled, Is.False, "An unowned anchor was vetoed while a hull was Cracked.");
                Assert.That(surveying.Cancelled, Is.False, "An unowned anchor was vetoed while a hull was Surveying.");
            }
        });

        await server.WaitPost(() => entMan.DeleteEntity(loose));
        await server.WaitRunTicks(1);

        await Cleanup(pair, site);
    }

    /// <summary>The veto reason is localised text, since SwitchOff substitutes it raw into the refusal popup.</summary>
    [Test]
    public async Task TheVetoReasonIsAlreadyLocalised()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);

        await server.WaitAssertion(() =>
        {
            var attempt = RaiseAttempt(entMan, site.Anchors[0]);

            Assert.That(attempt.Reason, Is.Not.Null, "Precondition: the attempt was refused with a reason.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(attempt.Reason, Does.Not.StartWith("wf-"), "The refusal handed back a locale id, not a sentence.");
                Assert.That(attempt.Reason, Does.Not.Contain("{"), "The refusal still carries an unsubstituted variable.");
            }
        });

        await Cleanup(pair, site);
    }

    // ------------------------------------------------------------------------------------------- ABORT INTERLOCK

    /// <summary>A forced switch-off during a spin-down is ignored, since it bypasses the cancellable veto.</summary>
    [Test]
    public async Task AForcedSwitchOffDuringASpinDownIsIgnored()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();
        var anchors = server.System<WFGravityAnchorSystem>();

        var site = await BuildExtracted(pair);

        await server.WaitPost(() =>
        {
            crackers.StartAbort((site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker)),
                WFCrackState.AnchorsPlaced);

            foreach (var anchor in site.Anchors)
            {
                anchors.ForceSwitchOff((anchor, entMan.GetComponent<WFGravityAnchorComponent>(anchor)));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracked),
                    "The admin disconnect path entered the protocol mid-spin-down.");
                Assert.That(comp.PendingAbort, Is.Not.Null, "The forced switch-off cancelled the spin-down.");
                Assert.That(comp.DisconnectArmed, Is.False, "The forced switch-off armed a pairing window anyway.");
                Assert.That(comp.EvacRunning, Is.False, "An evacuation started that no sweep branch could ever tick.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Entering Disconnecting clears a stale abort deadline, and no spin-down lands afterwards.</summary>
    [Test]
    public async Task EnteringDisconnectingClearsAPendingAbort()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var anchors = server.System<WFGravityAnchorSystem>();
        var events = server.System<WFAnchorTestEventSystem>();

        var site = await BuildExtracted(pair);
        var chunk = EntityUid.Invalid;
        var spinDown = TimeSpan.Zero;

        await server.WaitPost(() => chunk = FindChunk(entMan));
        await SoftenCrash(pair, chunk);

        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            // Short enough that the release lands inside the spin-down window.
            comp.EvacDuration = TimeSpan.FromSeconds(8);
            comp.AbortEnd = server.Timing.CurTime + comp.AbortSpinDown;
            spinDown = comp.AbortSpinDown;

            events.Clear();

            foreach (var anchor in site.Anchors)
            {
                anchors.ForceSwitchOff((anchor, entMan.GetComponent<WFGravityAnchorComponent>(anchor)));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Disconnecting), "The disconnect did not commit.");
                Assert.That(comp.PendingAbort, Is.Null, "The entry left a pending abort behind.");
                Assert.That(comp.AbortEnd, Is.EqualTo(TimeSpan.Zero), "The entry left a live abort deadline behind.");
                Assert.That(comp.EvacRunning, Is.True, "The evacuation never armed.");
            }
        });

        // Past the spin-down, where a missing interlock would let FinishAbort land.
        await server.WaitRunTicks(pair.SecondsToTicks((float)spinDown.TotalSeconds + 5f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(events.StateChanges.Any(change => change.New == WFCrackState.AnchorsPlaced), Is.False,
                    "A spin-down landed on the hull after it had already disconnected.");
                Assert.That(events.StateChanges.Any(change => change.New == WFCrackState.Released), Is.True,
                    "The evacuation never expired into Released.");
            }
        });

        await Cleanup(pair, site);
    }

    // --------------------------------------------------------------------------------------------- PAIRING WINDOW

    /// <summary>Two anchors off inside the window is the disconnect, and there is no other way to start one.</summary>
    [Test]
    public async Task TheSecondSwitchOffWithinTheWindowCommitsTheDisconnect()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var anchors = server.System<WFGravityAnchorSystem>();
        var events = server.System<WFAnchorTestEventSystem>();

        var site = await BuildExtracted(pair);

        await server.WaitPost(() =>
        {
            events.Clear();
            anchors.ForceSwitchOff((site.Anchors[0], entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0])));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.DisconnectArmed, Is.True, "The first switch-off did not arm the pairing window.");
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracked), "One anchor off already committed the disconnect.");
            }
        });

        await server.WaitPost(() =>
            anchors.ForceSwitchOff((site.Anchors[1], entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[1]))));

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Disconnecting), "The second switch-off did not commit.");
                Assert.That(comp.EvacRunning, Is.True, "The evacuation never armed.");
                Assert.That(comp.DisconnectArmed, Is.False, "The committed window is still armed.");
                Assert.That(comp.PendingAbort, Is.Null, "The entry left a pending abort behind.");
                Assert.That(
                    events.StateChanges.Any(change =>
                        change.Old == WFCrackState.Cracked && change.New == WFCrackState.Disconnecting),
                    Is.True, "No Cracked to Disconnecting change was announced.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>A lapsed pairing window turns the first anchor back on rather than stranding it.</summary>
    [Test]
    public async Task ALateSecondSwitchOffReArmsTheFirst()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var anchors = server.System<WFGravityAnchorSystem>();
        var events = server.System<WFAnchorTestEventSystem>();

        var site = await BuildExtracted(pair);

        await server.WaitPost(() =>
        {
            events.Clear();

            // Shortened before arming, since DisconnectEnd is stamped from it.
            entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).DisconnectWindow = TimeSpan.FromSeconds(1);
            anchors.ForceSwitchOff((site.Anchors[0], entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0])));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0]).State,
                    Is.EqualTo(WFAnchorState.Locked), "The lapsed window left its anchor stranded in Off.");
                Assert.That(comp.DisconnectArmed, Is.False, "The lapsed window is still armed.");
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracked), "A lapsed window moved the hull's stage.");
                Assert.That(events.ReArmed, Has.Count.EqualTo(1), "The re-arm edge was not announced exactly once.");
                Assert.That(events.ReArmed[0].Anchor, Is.EqualTo(site.Anchors[0]), "The re-arm named another anchor.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>An admin pulling the hull off Cracked ends the window too, and the anchor still comes back.</summary>
    [Test]
    public async Task TheWindowIsDroppedWhenTheHullLeavesCracked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var anchors = server.System<WFGravityAnchorSystem>();
        var events = server.System<WFAnchorTestEventSystem>();

        var site = await BuildExtracted(pair);

        await server.WaitPost(() =>
        {
            events.Clear();
            anchors.ForceSwitchOff((site.Anchors[0], entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0])));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).DisconnectArmed, Is.True,
                "Precondition: the pairing window armed."));

        await server.WaitPost(() => server.ConsoleHost.ExecuteCommand(null, "wfcracker state AnchorsLocked"));
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).DisconnectArmed, Is.False,
                    "The window survived the hull leaving Cracked.");
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0]).State,
                    Is.EqualTo(WFAnchorState.Locked), "The dropped window left its anchor stranded in Off.");
                Assert.That(events.ReArmed, Has.Count.EqualTo(1), "The re-arm edge was not announced exactly once.");
            }
        });

        await Cleanup(pair, site);
    }

    // ----------------------------------------------------------------------------------------------- DISCONNECTING

    /// <summary>Entering Disconnecting stops the grace countdown, whose klaxon would loop all round.</summary>
    [Test]
    public async Task DisconnectingDisarmsTheGraceCountdown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var anchors = server.System<WFGravityAnchorSystem>();

        var site = await BuildExtracted(pair);

        await FreezeCharge(pair, site.Centrifuge);
        await SetCharge(pair, site.Centrifuge, 0.5f);

        // A Cracked hull sweeps at 1 Hz, so SetCharge's second may not cover a full sweep.
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.GraceRunning, Is.True, "Precondition: a failing rotor armed the grace countdown.");
                Assert.That(comp.KlaxonStream, Is.Not.Null, "Precondition: the grace armed the klaxon.");
            }
        });

        await server.WaitPost(() =>
        {
            foreach (var anchor in site.Anchors)
            {
                anchors.ForceSwitchOff((anchor, entMan.GetComponent<WFGravityAnchorComponent>(anchor)));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Disconnecting), "The disconnect did not commit.");
                Assert.That(comp.GraceRunning, Is.False, "The disconnect left the grace countdown running.");
                Assert.That(comp.KlaxonStream, Is.Null, "The disconnect left the grace klaxon looping.");
                Assert.That(comp.GraceEnd, Is.EqualTo(TimeSpan.Zero), "The disconnect left a live grace deadline.");
                Assert.That(comp.Failing, Is.EqualTo(WFCrackFailure.None), "The disconnect left the failure flags set.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The hull and the chunk each get their own looping alarm.</summary>
    [Test]
    public async Task TheHullAlarmRunsWhileDisconnecting()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildDisconnecting(pair);

        await server.WaitAssertion(() =>
        {
            var hull = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            var chunk = entMan.GetComponent<WFPlanetChunkComponent>(FindChunk(entMan));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hull.EvacStream, Is.Not.Null, "The hull is not running an evacuation alarm.");
                Assert.That(entMan.EntityExists(hull.EvacStream!.Value), Is.True, "The hull's alarm stream is already dead.");
                Assert.That(chunk.Evacuating, Is.True, "The chunk never heard about the disconnect.");
                Assert.That(chunk.EvacStream, Is.Not.Null, "The chunk is not running an evacuation alarm.");
                Assert.That(entMan.EntityExists(chunk.EvacStream!.Value), Is.True, "The chunk's alarm stream is already dead.");
            }
        });

        await Cleanup(pair, site);
    }

    // ----------------------------------------------------------------------------------------------------- RELEASE

    /// <summary>The evacuation expiry pushes the chunk into transit at 0.98, still descending a second later.</summary>
    [Test]
    public async Task TheEvacuationExpiryDropsTheChunk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var events = server.System<WFAnchorTestEventSystem>();

        var site = await BuildDisconnecting(pair);
        var chunk = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            chunk = FindChunk(entMan);
            events.Clear();
        });

        await SoftenCrash(pair, chunk);

        var push = await PushRelease(pair, site, chunk);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(push.OnTransitMap, Is.True, "The chunk is not on a transit map, so the push never happened.");
            Assert.That(push.LeftOrbit, Is.True, "The chunk never left the orbit layer.");
            Assert.That(push.HasFaller, Is.True, "The push never made the chunk a faller.");
            Assert.That(push.Dropped, Is.True, "The release did not mark the chunk dropped.");
            Assert.That(push.BodyType, Is.Not.EqualTo(BodyType.Static),
                "The chunk was still static in the instant the push returned.");
            Assert.That(push.Progress, Is.EqualTo(0.98f).Within(0.01f),
                "The chunk did not enter transit just below the hull's own start progress.");
        }

        await server.WaitAssertion(() =>
            Assert.That(events.Releasing, Has.Count.EqualTo(1), "The release hook did not fire exactly once."));

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var progress = entMan.TryGetComponent(chunk, out CEZPhysicsComponent? zPhys) ? zPhys.LocalPosition : -1f;

            Assert.That(progress, Is.LessThan(0.9f), $"The chunk is not descending (progress {progress}).");
        });

        await Cleanup(pair, site);
    }

    /// <summary>The drop copies the per-tile crash tunables and suppresses the mis-centred central blast.</summary>
    [Test]
    public async Task DropWritesTheCrashTunables()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildDisconnecting(pair);
        var chunk = EntityUid.Invalid;

        await server.WaitPost(() => chunk = FindChunk(entMan));

        // Non-default values, so the faller carrying them proves the copy.
        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);
            comp.CrashTileIntensity = 0f;
            comp.CrashTileMaxIntensity = 3.5f;
        });

        await server.WaitRunTicks(1);

        var push = await PushRelease(pair, site, chunk);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(push.HasFaller, Is.True, "The push never made the chunk a faller.");
            Assert.That(push.CrashIntensityPerTile, Is.EqualTo(0f),
                "The engine's mis-centred central blast was not suppressed.");
            Assert.That(push.CrashTileIntensity, Is.EqualTo(0f).Within(0.001f),
                "The chunk's per-tile crash intensity was not copied onto the faller.");
            Assert.That(push.CrashTileMaxIntensity, Is.EqualTo(3.5f).Within(0.001f),
                "The chunk's per-tile crash cap was not copied onto the faller.");
        }

        await Cleanup(pair, site);
    }

    /// <summary>After release the lock comes off, the target is dropped and the settle timer runs out.</summary>
    [Test]
    public async Task TheHullEntersReleasedThenIdle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();
        var events = server.System<WFAnchorTestEventSystem>();

        var site = await BuildDisconnecting(pair);
        var chunk = EntityUid.Invalid;

        await server.WaitPost(() => chunk = FindChunk(entMan));
        await SoftenCrash(pair, chunk);

        await server.WaitPost(() =>
        {
            events.Clear();

            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            // Shortened before release, since ReleaseEnd is stamped from it.
            comp.ReleaseSettle = TimeSpan.FromSeconds(1);
            crackers.ReleaseNow((site.Cracker, comp));
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Released), "The drop did not hand the hull back.");
                Assert.That(comp.Locked, Is.False, "The released hull still thinks it is locked.");
                Assert.That(entMan.HasComponent<ForceAnchorComponent>(site.Cracker), Is.False,
                    "The release left the force anchor on.");
                Assert.That(comp.AnchorA, Is.Null, "The release left the first anchor targeted.");
                Assert.That(comp.AnchorB, Is.Null, "The release left the second anchor targeted.");
                Assert.That(comp.PendingAbort, Is.Null, "The release started a spin-down.");
                Assert.That(comp.Chunk, Is.Null, "The hull still claims a chunk it has let go of.");
                Assert.That(comp.EvacRunning, Is.False, "The release left the evacuation running.");
                Assert.That(comp.EvacStream, Is.Null, "The release left the hull alarm looping.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    events.StateChanges.Any(change =>
                        change.Old == WFCrackState.Released && change.New == WFCrackState.Idle),
                    Is.True, "The settled hull never left Released.");
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                    Is.EqualTo(WFCrackState.Surveying),
                    "A hull back in orbit did not pick its survey up again on the following sweep.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Deleting the chunk and its anchors after release does not spin down the finished cut.</summary>
    [Test]
    public async Task NoAbortFiresWhenTheChunkAndItsAnchorsAreDeleted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();
        var events = server.System<WFAnchorTestEventSystem>();

        var site = await BuildDisconnecting(pair);
        var chunk = EntityUid.Invalid;

        await server.WaitPost(() => chunk = FindChunk(entMan));
        await SoftenCrash(pair, chunk);

        await server.WaitPost(() =>
        {
            events.Clear();
            crackers.ReleaseNow((site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker)));
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.Released), "Precondition: the hull was handed back."));

        await server.WaitPost(() =>
        {
            if (entMan.EntityExists(chunk))
                entMan.DeleteEntity(chunk);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).PendingAbort, Is.Null,
                    "Deleting the released chunk started a spin-down.");
                Assert.That(events.StateChanges.Any(change => change.New == WFCrackState.AnchorsPlaced), Is.False,
                    "A spin-down landed on a hull that had already released its chunk.");
            }
        });

        await Cleanup(pair, site);
    }

    // ----------------------------------------------------------------------------------------- LANDING AND CLEANUP

    /// <summary>The wreck settles into its own crater: pose re-asserted, body frozen and both loops stopped.</summary>
    [Test]
    public async Task TheChunkLandsAndStopsItsDropLoop()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var events = server.System<WFAnchorTestEventSystem>();

        var site = await BuildDisconnecting(pair);
        var chunk = await ArmLanding(pair, NoCleanup);

        await server.WaitPost(() => events.Clear());
        await Release(pair, site);

        Assert.That(await WaitForLanding(pair, chunk), Is.True, "The released chunk never settled out of transit.");

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);
            var map = entMan.GetComponent<TransformComponent>(chunk).MapUid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(map, Is.EqualTo(site.Ground), "The chunk did not come down on the ground layer.");
                Assert.That(comp.Landed, Is.True, "The landing was never recorded.");
                Assert.That(comp.DropStream, Is.Null, "The falling-rock loop is still playing on the wreck.");
                Assert.That(comp.EvacStream, Is.Null, "The evacuation alarm is still playing on the wreck.");
                Assert.That(comp.Evacuating, Is.False, "The wreck is still evacuating.");
                Assert.That(entMan.GetComponent<PhysicsComponent>(chunk).BodyType, Is.EqualTo(BodyType.Static),
                    "The wreck is still a dynamic body, so friction will slide it off its own crater.");
                Assert.That(entMan.HasComponent<PreventGridAnchorChangesComponent>(chunk), Is.True,
                    "The wreck was not pinned against anchor changes.");
                Assert.That(events.ChunksLanded, Has.Count.EqualTo(1), "The landing hook did not fire exactly once.");
                Assert.That(events.ChunksLanded[0].Chunk, Is.EqualTo(chunk), "The landing hook named another chunk.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The scar keeps the hole open through the landing and pins only the disc, not the footprint.</summary>
    [Test]
    public async Task TheHoleSurvivesTheLanding()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildDisconnecting(pair);
        var chunk = await ArmLanding(pair, NoCleanup);

        var centre = Vector2.Zero;
        var radius = 0f;
        var wideBefore = 0;

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);
            centre = comp.HoleCentre;
            radius = comp.Radius;
            wideBefore = PinnedCount(pair, site.Ground, WideIndices(entMan, site.Ground, centre, radius));
        });

        await Release(pair, site);

        Assert.That(await WaitForLanding(pair, chunk), Is.True, "The released chunk never settled out of transit.");

        await server.WaitAssertion(() =>
        {
            var disc = DiscIndices(entMan, site.Ground, centre, radius);
            var filled = HoleTiles(entMan, site.Ground, centre, radius).Count(entry => !entry.Tile.IsEmpty);
            var pinned = PinnedCount(pair, site.Ground, disc);
            var wideAfter = PinnedCount(pair, site.Ground, WideIndices(entMan, site.Ground, centre, radius));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(disc, Is.Not.Empty, "Precondition: the cut circle covers tiles at all.");
                Assert.That(filled, Is.Zero, $"{filled} of {disc.Count} hole tiles were refilled by the landing.");
                Assert.That(pinned, Is.EqualTo(disc.Count), "The landing left part of the hole unpinned.");
                Assert.That(wideAfter, Is.LessThanOrEqualTo(wideBefore + disc.Count),
                    "The landing pinned far more than the disc, which freezes the whole footprint against regeneration.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The scar lives on the ground grid, not the chunk, because it has to outlive the chunk.</summary>
    [Test]
    public async Task ScarIsRecordedAtExtraction()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildExtracted(pair);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(FindChunk(entMan));

            Assert.That(entMan.TryGetComponent(site.Ground, out WFCrackScarComponent? scar), Is.True,
                "The ground layer carries no scar, so nothing would ever re-open the hole.");
            Assert.That(scar!.Scars, Has.Count.EqualTo(1), "The extraction recorded the wrong number of scars.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(scar.Scars[0].Centre, Is.EqualTo(comp.HoleCentre), "The scar is centred somewhere else.");
                Assert.That(scar.Scars[0].Radius, Is.EqualTo(comp.Radius).Within(0.001f),
                    "The scar has a different radius from the hole it records.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The cleanup-immune wreck is deleted after its delay and the crater stays.</summary>
    [Test]
    public async Task TheWreckIsClearedAndTheCraterStays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildDisconnecting(pair);
        // Crushed acid-blooded site threats would pry up rim tiles and their decals, so clear them first.
        await ClearSiteThreats(pair, site);
        var chunk = await ArmLanding(pair, FastCleanup);

        var centre = Vector2.Zero;
        var radius = 0f;
        var rim = new List<uint>();

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);
            centre = comp.HoleCentre;
            radius = comp.Radius;
            rim.AddRange(comp.RimDecals);

            Assert.That(rim, Is.Not.Empty, "Precondition: the rim was decalled at all.");
        });

        await Release(pair, site);

        Assert.That(await WaitForCleanup(pair, chunk), Is.True, "The wreck was never cleaned up.");

        await server.WaitAssertion(() =>
        {
            var disc = DiscIndices(entMan, site.Ground, centre, radius);
            var filled = HoleTiles(entMan, site.Ground, centre, radius).Count(entry => !entry.Tile.IsEmpty);
            var pinned = PinnedCount(pair, site.Ground, disc);
            var decals = GroundDecals(pair, site.Ground, centre, radius);
            var lost = rim.Count(id => !decals.Contains(id));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.EntityExists(chunk), Is.False, "The wreck is still sitting on the ground layer.");
                Assert.That(filled, Is.Zero, $"{filled} of {disc.Count} hole tiles closed over when the wreck was removed.");
                Assert.That(pinned, Is.EqualTo(disc.Count), "The cleanup left part of the hole unpinned.");
                Assert.That(lost, Is.Zero, $"{lost} of {rim.Count} rim decals did not survive the landing and cleanup.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>A chunk dropped by the hull's own fall is cleaned up too, not only one released.</summary>
    [Test]
    public async Task TheHullFallDropIsAlsoCleanedUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();

        var site = await BuildExtracted(pair);
        var chunk = await ArmLanding(pair, FastCleanup);

        await server.WaitPost(() =>
        {
            crackers.Fall((site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker)));

            // Zero the hull's own crash too, which SoftenCrash does not cover.
            if (entMan.TryGetComponent(site.Cracker, out CEZGridFallerComponent? faller))
            {
                faller.CrashIntensityPerTile = 0f;
                faller.CrashTileIntensity = 0f;
            }
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetChunkComponent>(chunk).Dropped, Is.True,
                "Precondition: the hull's fall took its chunk with it."));

        Assert.That(await WaitForCleanup(pair, chunk), Is.True,
            "A chunk dropped by the hull's own fall was left on the ground layer for good.");

        await Cleanup(pair, site);
    }

    // ---------------------------------------------------------------------------------------------------- WATCHDOG

    /// <summary>An orphaned chunk dropped by the watchdog also gets the evacuation alarm, with no countdown.</summary>
    [Test]
    public async Task TheWatchdogDropPlaysTheEvacuationAlarm()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildExtracted(pair);
        var chunk = EntityUid.Invalid;

        await server.WaitPost(() => chunk = FindChunk(entMan));
        await SoftenCrash(pair, chunk);

        // Drop the watchdog's five second grace so three seconds covers two sweeps.
        await server.WaitAssertion(() =>
        {
            Assert.That(chunk, Is.Not.EqualTo(EntityUid.Invalid), "Precondition: a disc was cut to abandon.");
            entMan.GetComponent<WFPlanetChunkComponent>(chunk).WatchdogGrace = TimeSpan.Zero;
        });

        await server.WaitPost(() => entMan.DeleteEntity(site.Cracker));
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Dropped, Is.True, "The orphaned chunk was never dropped.");
                Assert.That(comp.Evacuating, Is.True, "The watchdog drop started no evacuation alarm.");
                Assert.That(comp.EvacStream, Is.Not.Null, "The watchdog drop started no alarm loop.");
                Assert.That(entMan.EntityExists(comp.EvacStream!.Value), Is.True, "The alarm loop is already dead.");
            }
        });

        await Cleanup(pair, site);
    }

    // ------------------------------------------------------------------------------------------ CONSOLE AND COMMANDS

    /// <summary>Both countdowns derive from their deadlines, so they count down between sweeps.</summary>
    [Test]
    public async Task ConsoleStateCarriesBothCountdowns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var anchors = server.System<WFGravityAnchorSystem>();

        var site = await BuildExtracted(pair);

        await server.WaitPost(() =>
            anchors.ForceSwitchOff((site.Anchors[0], entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0]))));

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            var state = ReadConsoleState(pair, site);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(state.DisconnectArmed, Is.True, "The console does not report the armed pairing window.");
                Assert.That(state.DisconnectRemaining, Is.GreaterThan(TimeSpan.Zero),
                    "The console reports an armed window with nothing left on it.");
                Assert.That(state.EvacRunning, Is.False, "The console reports an evacuation before one has started.");
            }
        });

        await server.WaitPost(() =>
            anchors.ForceSwitchOff((site.Anchors[1], entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[1]))));

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            var state = ReadConsoleState(pair, site);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(state.EvacRunning, Is.True, "The console does not report the evacuation.");
                Assert.That(state.EvacRemaining, Is.GreaterThan(TimeSpan.Zero),
                    "The console reports an evacuation with nothing left on it.");
                Assert.That(state.DisconnectArmed, Is.False, "The console still reports the committed window as armed.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The rearm and release subcommands succeed; refusals can't be driven, as errors fail the test.</summary>
    [Test]
    public async Task TheRearmAndReleaseSubcommandsWork()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var anchors = server.System<WFGravityAnchorSystem>();

        var site = await BuildExtracted(pair);
        var chunk = EntityUid.Invalid;

        await server.WaitPost(() => chunk = FindChunk(entMan));
        await SoftenCrash(pair, chunk);

        await server.WaitPost(() =>
            anchors.ForceSwitchOff((site.Anchors[0], entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0]))));

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0]).State,
                    Is.EqualTo(WFAnchorState.Off), "Precondition: the first anchor is off.");
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).DisconnectArmed, Is.True,
                    "Precondition: the pairing window armed.");
            }
        });

        await server.WaitPost(() => server.ConsoleHost.ExecuteCommand(null, "wfcracker rearm"));
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0]).State,
                    Is.EqualTo(WFAnchorState.Locked), "`wfcracker rearm` left the anchor in Off.");
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).DisconnectArmed, Is.False,
                    "`wfcracker rearm` left the pairing window armed.");
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                    Is.EqualTo(WFCrackState.Cracked), "`wfcracker rearm` moved the hull's stage.");
            }
        });

        await server.WaitPost(() =>
        {
            foreach (var anchor in site.Anchors)
            {
                anchors.ForceSwitchOff((anchor, entMan.GetComponent<WFGravityAnchorComponent>(anchor)));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.Disconnecting), "Precondition: the re-armed pair can disconnect again."));

        await server.WaitPost(() => server.ConsoleHost.ExecuteCommand(null, "wfcracker release"));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                    Is.EqualTo(WFCrackState.Released), "`wfcracker release` did not hand the hull back.");
                Assert.That(entMan.GetComponent<WFPlanetChunkComponent>(chunk).Dropped, Is.True,
                    "`wfcracker release` did not let the chunk go.");
            }
        });

        await Cleanup(pair, site);
    }

    // ------------------------------------------------------------------------------------------------------ LOCALE

    /// <summary>Every disconnect locale key resolves with its arguments filled in.</summary>
    [Test]
    public async Task EveryDisconnectLocaleKeyResolves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var loc = server.ResolveDependency<ILocalizationManager>();

            using (Assert.EnterMultipleScope())
            {
                foreach (var (key, args) in DisconnectKeys)
                {
                    var text = loc.GetString(key, args);

                    Assert.That(text, Is.Not.EqualTo(key), $"{key} does not resolve to anything.");
                    Assert.That(text, Does.Not.Contain("{"), $"{key} still carries an unsubstituted variable.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every disconnect locale key, with a dummy for each variable it takes.</summary>
    private static readonly (string Key, (string, object)[] Args)[] DisconnectKeys =
    {
        ("wf-anchor-off-unwrench", Array.Empty<(string, object)>()),
        ("wf-anchor-off-not-cracked", Array.Empty<(string, object)>()),
        ("wf-anchor-off-aborting", Array.Empty<(string, object)>()),
        ("wf-anchor-off-not-on-chunk", Array.Empty<(string, object)>()),
        ("wf-crack-console-disconnect", new (string, object)[] { ("time", "0:59") }),
        ("wf-crack-console-evac", new (string, object)[] { ("time", "0:59") }),
        ("wf-crack-announce-sender", Array.Empty<(string, object)>()),
        ("wf-crack-disconnect-armed", new (string, object)[] { ("seconds", 60) }),
        ("wf-crack-disconnect-warning", new (string, object)[] { ("seconds", 15) }),
        ("wf-crack-disconnect-lapsed", Array.Empty<(string, object)>()),
        ("wf-crack-disconnect-committed", new (string, object)[] { ("seconds", 60) }),
        ("wf-crack-evac-warning", new (string, object)[] { ("seconds", 30) }),
        ("wf-crack-released", Array.Empty<(string, object)>()),
        ("cmd-wfcracker-rearmed", new (string, object)[] { ("grid", "grid") }),
        ("cmd-wfcracker-released", new (string, object)[] { ("grid", "grid") }),
        ("cmd-wfcracker-help", new (string, object)[] { ("command", "wfcracker") }),
        ("cmd-wfcracker-invalid-args", Array.Empty<(string, object)>()),
        ("cmd-wfcracker-hint-sub", Array.Empty<(string, object)>()),
        ("wf-chunk-evac-announce", new (string, object)[] { ("seconds", 60) }),
        ("wf-chunk-evac-popup", Array.Empty<(string, object)>()),
        ("wf-chunk-evac-warning", new (string, object)[] { ("seconds", 30) }),
        ("wf-chunk-evac-orphan", Array.Empty<(string, object)>()),
    };

    // ----------------------------------------------------------------------------------------------------- HELPERS

    /// <summary>Raises the broadcast switch-off attempt by hand and hands it back for inspection.</summary>
    private static WFAnchorSwitchOffAttemptEvent RaiseAttempt(IEntityManager entMan, EntityUid anchor)
    {
        // Broadcast, as SwitchOff raises it; the veto has no directed subscription.
        var attempt = new WFAnchorSwitchOffAttemptEvent(anchor, anchor);
        entMan.EventBus.RaiseEvent(EventSource.Local, attempt);
        return attempt;
    }

    /// <summary>One localised string, resolved on the server the way the production code resolves it.</summary>
    private static string Localise(TestPair pair, string key)
    {
        return pair.Server.ResolveDependency<ILocalizationManager>().GetString(key);
    }

    /// <summary>Deletes every site threat the anchors spawned, turrets too, so a landing crushes nothing.</summary>
    private static async Task ClearSiteThreats(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            foreach (var anchor in site.Anchors)
            {
                if (!entMan.TryGetComponent(anchor, out WFFissureSpawnerComponent? spawner))
                    continue;

                foreach (var threat in spawner.Spawned)
                {
                    if (entMan.EntityExists(threat))
                        entMan.DeleteEntity(threat);
                }

                spawner.Spawned.Clear();
                spawner.Live.Clear();
            }
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>Finds the extracted chunk, softens its crash and sets its cleanup delay.</summary>
    private static async Task<EntityUid> ArmLanding(TestPair pair, TimeSpan cleanupDelay)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var chunk = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            chunk = FindChunk(entMan);
            Assert.That(chunk, Is.Not.EqualTo(EntityUid.Invalid), "Precondition: a disc was cut to drop.");
        });

        await SoftenCrash(pair, chunk);

        await server.WaitPost(() =>
            entMan.GetComponent<WFPlanetChunkComponent>(chunk).CleanupDelay = cleanupDelay);

        await server.WaitRunTicks(1);
        return chunk;
    }

    /// <summary>Lets the chunk go through the same entry point the evacuation expiry uses.</summary>
    private static async Task Release(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();

        await server.WaitPost(() =>
            crackers.ReleaseNow((site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker))));

        await server.WaitRunTicks(1);
    }

    /// <summary>Releases the chunk and reads the push back in the same callback, before a frame can undo it.</summary>
    private static async Task<ReleaseResult> PushRelease(TestPair pair, CrackerSite site, EntityUid chunk)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();
        var result = new ReleaseResult();

        await server.WaitPost(() =>
        {
            crackers.ReleaseNow((site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker)));

            var mapUid = entMan.GetComponent<TransformComponent>(chunk).MapUid;

            result.OnTransitMap = entMan.HasComponent<CEZTransitMapComponent>(mapUid);
            result.LeftOrbit = !entMan.HasComponent<WFOrbitLayerComponent>(mapUid);
            result.HasFaller = entMan.TryGetComponent(chunk, out CEZGridFallerComponent? faller);
            result.CrashIntensityPerTile = faller?.CrashIntensityPerTile ?? -1f;
            result.CrashTileIntensity = faller?.CrashTileIntensity ?? -1f;
            result.CrashTileMaxIntensity = faller?.CrashTileMaxIntensity ?? -1f;
            result.Dropped = entMan.GetComponent<WFPlanetChunkComponent>(chunk).Dropped;
            result.Progress = entMan.TryGetComponent(chunk, out CEZPhysicsComponent? zPhys) ? zPhys.LocalPosition : -1f;
            result.BodyType = entMan.TryGetComponent(chunk, out PhysicsComponent? body) ? body.BodyType : BodyType.Static;
        });

        await server.WaitRunTicks(1);
        return result;
    }

    /// <summary>Ticks a second at a time until the chunk records its landing, or gives up.</summary>
    private static async Task<bool> WaitForLanding(TestPair pair, EntityUid chunk, float seconds = 25f)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var landed = false;

        for (var i = 0; i < (int)seconds && !landed; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitPost(() =>
                landed = entMan.TryGetComponent(chunk, out WFPlanetChunkComponent? comp) && comp.Landed);
        }

        return landed;
    }

    /// <summary>Ticks a second at a time until the wreck is gone, or gives up.</summary>
    private static async Task<bool> WaitForCleanup(TestPair pair, EntityUid chunk, float seconds = 30f)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var gone = false;

        for (var i = 0; i < (int)seconds && !gone; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitPost(() => gone = !entMan.EntityExists(chunk));
        }

        return gone;
    }

    /// <summary>Every decal id near the cut, via the decal system since DecalGridComponent is access-locked.</summary>
    private static HashSet<uint> GroundDecals(TestPair pair, EntityUid ground, Vector2 centre, float radius)
    {
        var decals = pair.Server.System<DecalSystem>();
        var span = (radius + 4f) * 2f;
        var found = new HashSet<uint>();

        foreach (var (id, _) in decals.GetDecalsIntersecting(ground, Box2.CenteredAround(centre, new Vector2(span, span))))
        {
            found.Add(id);
        }

        return found;
    }

    /// <summary>A sample covering the chunk's whole square landing footprint.</summary>
    private static List<Vector2i> WideIndices(IEntityManager entMan, EntityUid ground, Vector2 centre, float radius)
    {
        return DiscIndices(entMan, ground, centre, radius + 6f);
    }

    /// <summary>Anything mid-transit goes before the stack it was falling into.</summary>
    private static async Task Cleanup(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            foreach (var uid in new List<EntityUid> { FindChunk(entMan), site.Cracker })
            {
                if (uid != EntityUid.Invalid && entMan.EntityExists(uid))
                    entMan.DeleteEntity(uid);
            }
        });

        await server.WaitRunTicks(1);

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>The release as read in the same tick it happened.</summary>
    private sealed class ReleaseResult
    {
        /// <summary>Whether the chunk's map was a transit map.</summary>
        public bool OnTransitMap;

        /// <summary>Whether the chunk's map was no longer the planet's orbit layer.</summary>
        public bool LeftOrbit;

        /// <summary>Whether the chunk carried a faller.</summary>
        public bool HasFaller;

        /// <summary>The faller's suppressed central blast, which must be exactly zero.</summary>
        public float CrashIntensityPerTile;

        /// <summary>The faller's per-tile crash intensity, copied off the chunk component.</summary>
        public float CrashTileIntensity;

        /// <summary>The faller's per-tile crash cap, copied off the chunk component.</summary>
        public float CrashTileMaxIntensity;

        /// <summary>Whether the chunk recorded itself as dropped.</summary>
        public bool Dropped;

        /// <summary>Altitude within the transit level, 1 at the top and 0 at the bottom.</summary>
        public float Progress;

        /// <summary>The chunk's body type, which has to be Dynamic before the transit call or nothing falls.</summary>
        public BodyType BodyType;
    }
}
