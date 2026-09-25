#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.PlanetCracker.Anchors;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Repairable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>Cutting timers: crack duration, damage pause, abort spin-down, grace countdown and stage walk.</summary>
[TestFixture]
[TestOf(typeof(WFCrackerSystem))]
public sealed class CrackTimerTest
{
    /// <summary>Structural damage neither the anchor nor the projector modifier sets can soak.</summary>
    private const string Blunt = "Blunt";

    /// <summary>Damage that clears the anchor's 300 Breakage trigger without reaching its Destruction one.</summary>
    private const float AnchorBreakingDamage = 320f;

    /// <summary>Damage that clears the projector's 250 Breakage trigger.</summary>
    private const float ProjectorBreakingDamage = 280f;

    /// <summary>Spin-down the abort tests shorten the shipped thirty seconds to.</summary>
    private static readonly TimeSpan ShortSpinDown = TimeSpan.FromSeconds(2);

    /// <summary>Grace window the expiry test shortens the shipped five minutes to.</summary>
    private static readonly TimeSpan ShortGrace = TimeSpan.FromSeconds(2);

    /// <summary>Duration spans 5.6 minutes (d=16, tier 4) to twenty minutes (d=40, tier 1).</summary>
    [Test]
    public void CrackDurationFollowsTheFormula()
    {
        var baseTime = TimeSpan.FromMinutes(12);
        const float reference = 24f;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SharedWFCrackerSystem.GetCrackDuration(16f, 1f, baseTime, reference).TotalMinutes,
                Is.EqualTo(8d).Within(0.01d), "Sixteen tiles at tier 1 is not eight minutes.");
            Assert.That(SharedWFCrackerSystem.GetCrackDuration(24f, 1f, baseTime, reference).TotalMinutes,
                Is.EqualTo(12d).Within(0.01d), "The reference distance at tier 1 is not the base time.");
            Assert.That(SharedWFCrackerSystem.GetCrackDuration(40f, 1f, baseTime, reference).TotalMinutes,
                Is.EqualTo(20d).Within(0.01d), "Forty tiles at tier 1 is not the design's twenty minute ceiling.");
            Assert.That(SharedWFCrackerSystem.GetCrackDuration(16f, 0.7f, baseTime, reference).TotalMinutes,
                Is.EqualTo(5.6d).Within(0.01d), "Sixteen tiles at tier 4 is not the design's 5.6 minute floor.");
        }
    }

    /// <summary>The part multiplier is the mean of both projectors, checked on a mixed pair.</summary>
    [Test]
    public async Task PartMultiplierIsTheMeanOfBothProjectors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();

        var site = await BuildCrackerInOrbit(pair);

        await server.WaitPost(() =>
        {
            // Tier 4 is 0.70, tier 1 is 1.0.
            entMan.GetComponent<WFGravityProjectorComponent>(site.Projectors[0]).CrackTimeMultiplier = 0.7f;
            entMan.GetComponent<WFGravityProjectorComponent>(site.Projectors[1]).CrackTimeMultiplier = 1f;
        });

        await server.WaitAssertion(() =>
        {
            var multiplier = crackers.GetPartMultiplier(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(multiplier, Is.EqualTo(0.85f).Within(0.001f),
                    "A mixed pair does not use the arithmetic mean of both multipliers.");
                Assert.That(multiplier, Is.Not.EqualTo(0.7f).Within(0.001f),
                    "The multiplier is the minimum, which makes a single-projector upgrade worth nothing.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A damaged anchor pauses the cut and banks the remainder, so a repair resumes from there.</summary>
    [Test]
    public async Task DamagePausesAndResumesTheCrack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var damageable = server.System<DamageableSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);

        var anchor = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            anchor = entMan.GetEntity(comp.AnchorA!.Value);

            var anchorComp = entMan.GetComponent<WFGravityAnchorComponent>(anchor);

            // Past the damaged line but below Breakage, which would abort instead.
            damageable.TryChangeDamage(anchor,
                Damage(proto, anchorComp.BreakDamage * anchorComp.DamageFraction + 10f), true);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        var banked = TimeSpan.Zero;

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            banked = comp.CrackRemaining;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(anchor).Damaged, Is.True,
                    "Precondition: the anchor is past its damage threshold.");
                Assert.That(comp.CrackPaused, Is.True, "A damaged targeted anchor did not hold the cut.");
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracking), "The damage aborted the cut.");
                Assert.That(banked, Is.GreaterThan(TimeSpan.Zero), "The banked remainder is empty.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.CrackRemaining, Is.EqualTo(banked), "The banked remainder kept falling while paused.");
                Assert.That(ReadConsoleState(pair, site).CrackRemaining, Is.EqualTo(banked),
                    "The console reads a falling remainder on a paused cut.");
                Assert.That(ReadConsoleState(pair, site).CrackPaused, Is.True,
                    "The console does not report the pause.");
            }
        });

        await server.WaitPost(() =>
            damageable.SetAllDamage(anchor, entMan.GetComponent<DamageableComponent>(anchor), 0));

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.CrackPaused, Is.False, "The repair did not lift the pause.");
                Assert.That(comp.CrackRemaining, Is.LessThanOrEqualTo(banked),
                    "The remainder went up across the resume.");

                // An unbanked deadline would have lost the paused seconds.
                Assert.That(comp.CrackRemaining, Is.GreaterThan(banked - TimeSpan.FromSeconds(2.5)),
                    "The cut resumed from its original deadline rather than the banked remainder.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A projector returns to Firing after a power blip, as the sweep reconciles its state.</summary>
    [Test]
    public async Task ProjectorReturnsToFiringAfterAPowerBlip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var damageable = server.System<DamageableSystem>();
        var receiver = server.System<SharedPowerReceiverSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);

        var projector = site.Projectors[0];

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var uid in site.Projectors)
                {
                    Assert.That(entMan.GetComponent<WFGravityProjectorComponent>(uid).State,
                        Is.EqualTo(WFProjectorState.Firing), "A cutting hull's projectors should be firing.");
                }
            }
        });

        // Restoring NeedsPower on an uncabled hull is a brownout.
        await server.WaitPost(() => receiver.SetNeedsPower(projector, true));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(receiver.IsPowered(projector), Is.False, "Precondition: the projector lost power.");
                Assert.That(entMan.GetComponent<WFGravityProjectorComponent>(projector).State,
                    Is.EqualTo(WFProjectorState.Off), "An unpowered projector should read as off.");
            }
        });

        await server.WaitPost(() => receiver.SetNeedsPower(projector, false));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var row = ReadConsoleState(pair, site).Projectors[0];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityProjectorComponent>(projector).State,
                    Is.EqualTo(WFProjectorState.Firing),
                    "The projector was stranded at idle after the power came back, blanking the emitter for the cut.");
                Assert.That(row.State, Is.EqualTo(WFProjectorState.Firing),
                    "The console's beam indicator never came back.");
                Assert.That(row.Powered, Is.True, "The console still reports the projector unpowered.");
            }
        });

        await server.WaitPost(() => damageable.TryChangeDamage(projector,
            Damage(proto, ProjectorBreakingDamage), true));

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityProjectorComponent>(projector).Broken, Is.True,
                    "Precondition: the projector broke.");
                Assert.That(entMan.GetComponent<WFGravityProjectorComponent>(projector).State,
                    Is.EqualTo(WFProjectorState.Broken), "A broken projector should read as broken.");
            }
        });

        await server.WaitPost(() =>
        {
            damageable.SetAllDamage(projector, entMan.GetComponent<DamageableComponent>(projector), 0);
            var repaired = new RepairedEvent((projector, entMan.GetComponent<RepairableComponent>(projector)), projector);
            entMan.EventBus.RaiseLocalEvent(projector, ref repaired);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var row = ReadConsoleState(pair, site).Projectors[0];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityProjectorComponent>(projector).State,
                    Is.EqualTo(WFProjectorState.Firing),
                    "The repaired projector was stranded at idle for the rest of the cut.");
                Assert.That(row.Broken, Is.False, "The console still reports the projector broken.");
                Assert.That(row.State, Is.EqualTo(WFProjectorState.Firing),
                    "The console's beam indicator never came back after the repair.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A broken anchor spins down to AnchorsPlaced, keeping the lock until the spin-down ends.</summary>
    [Test]
    public async Task BrokenAnchorAbortsToAnchorsPlacedAfterTheSpinDown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var damageable = server.System<DamageableSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);

        var anchor = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            // Shortened before the loss, since AbortEnd is stamped from it.
            comp.AbortSpinDown = ShortSpinDown;
            anchor = entMan.GetEntity(comp.AnchorA!.Value);

            damageable.TryChangeDamage(anchor, Damage(proto, AnchorBreakingDamage), true);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            var state = ReadConsoleState(pair, site);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(anchor).State,
                    Is.EqualTo(WFAnchorState.Broken), "Precondition: the anchor broke.");
                Assert.That(comp.PendingAbort, Is.EqualTo(WFCrackState.AnchorsPlaced),
                    "A broken anchor should spin down to anchors-placed.");
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracking),
                    "The hull left the cut before its spin-down finished.");
                Assert.That(comp.Locked, Is.True, "The hull dropped its lock during the spin-down.");
                Assert.That(entMan.HasComponent<ForceAnchorComponent>(site.Cracker), Is.True,
                    "The hull was released during the spin-down.");
                Assert.That(state.PendingAbort, Is.EqualTo(WFCrackState.AnchorsPlaced),
                    "The console does not report where the abort is heading.");
                Assert.That(state.AbortRemaining, Is.GreaterThan(TimeSpan.Zero),
                    "The console reports no spin-down countdown.");
            }
        });

        // Repaired mid-spin-down; only the spin-down's own expiry clears PendingAbort.
        await server.WaitPost(() =>
        {
            damageable.SetAllDamage(anchor, entMan.GetComponent<DamageableComponent>(anchor), 0);
            var repaired = new RepairedEvent((anchor, entMan.GetComponent<RepairableComponent>(anchor)), anchor);
            entMan.EventBus.RaiseLocalEvent(anchor, ref repaired);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFCrackState.AnchorsPlaced),
                    "The spin-down did not land on anchors-placed.");
                Assert.That(comp.PendingAbort, Is.Null, "The finished spin-down left its pending state behind.");
                Assert.That(comp.AnchorA, Is.Null, "The abort did not drop the first anchor.");
                Assert.That(comp.AnchorB, Is.Null, "The abort did not drop the second anchor.");
                Assert.That(comp.Locked, Is.False, "The hull still thinks it is locked.");
                Assert.That(entMan.HasComponent<ForceAnchorComponent>(site.Cracker), Is.False,
                    "The force anchor was never removed.");
                Assert.That(entMan.HasComponent<PreventGridAnchorChangesComponent>(site.Cracker), Is.False,
                    "The anchor-change block was never removed.");
                Assert.That(entMan.GetComponent<PhysicsComponent>(site.Cracker).BodyType,
                    Is.Not.EqualTo(BodyType.Static),
                    "The released hull is still a static body, so a fall would never move it.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A destroyed anchor is gone for good, so the spin-down lands on Surveying instead.</summary>
    [Test]
    public async Task DestroyedAnchorAbortsToSurveying()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);

        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            comp.AbortSpinDown = ShortSpinDown;
            entMan.QueueDeleteEntity(entMan.GetEntity(comp.AnchorA!.Value));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.PendingAbort, Is.EqualTo(WFCrackState.Surveying),
                    "A destroyed anchor should spin down to surveying.");
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracking),
                    "The hull left the cut before its spin-down finished.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Surveying),
                    "The spin-down did not land on surveying.");
                Assert.That(comp.PendingAbort, Is.Null, "The finished spin-down left its pending state behind.");
                Assert.That(comp.AnchorA, Is.Null, "The abort did not drop the first anchor.");
                Assert.That(comp.AnchorB, Is.Null, "The abort did not drop the second anchor.");
                Assert.That(entMan.HasComponent<ForceAnchorComponent>(site.Cracker), Is.False,
                    "The force anchor was never removed.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The at-full latch closes at FullOn and opens below FullOff, so the grace does not chatter.</summary>
    [Test]
    public async Task GraceArmsOnCentrifugeLossAndResetsWithHysteresis()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);
        await FreezeCharge(pair, site.Centrifuge);

        await SetCharge(pair, site.Centrifuge, 0.96f);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFCentrifugeComponent>(site.Centrifuge).AtFull, Is.True,
                    "The latch let go between FullOff and FullOn, so there is no hysteresis.");
                Assert.That(comp.GraceRunning, Is.False, "The grace countdown armed inside the hysteresis band.");
            }
        });

        await SetCharge(pair, site.Centrifuge, 0.94f);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            var state = ReadConsoleState(pair, site);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFCentrifugeComponent>(site.Centrifuge).AtFull, Is.False,
                    "The latch held below FullOff.");
                Assert.That(comp.GraceRunning, Is.True, "A rotor below full did not arm the grace countdown.");
                Assert.That(comp.Failing & WFCrackFailure.Centrifuge, Is.EqualTo(WFCrackFailure.Centrifuge),
                    "The centrifuge is not named as the failing condition.");
                Assert.That(state.GraceRunning, Is.True, "The console does not report the grace countdown.");
                Assert.That(state.GraceRemaining, Is.GreaterThan(TimeSpan.Zero),
                    "The console reports no time left on the grace countdown.");
            }
        });

        await SetCharge(pair, site.Centrifuge, 0.96f);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFCentrifugeComponent>(site.Centrifuge).AtFull, Is.False,
                    "The latch closed below FullOn.");
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).GraceRunning, Is.True,
                    "The grace countdown reset inside the hysteresis band.");
            }
        });

        await SetCharge(pair, site.Centrifuge, 0.99f);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFCentrifugeComponent>(site.Centrifuge).AtFull, Is.True,
                    "The latch never closed at FullOn.");
                Assert.That(comp.GraceRunning, Is.False, "A rotor back at full did not clear the grace countdown.");
                Assert.That(comp.Failing, Is.EqualTo(WFCrackFailure.None), "The failure flags were not cleared.");
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracking), "The cut did not survive the scare.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The grace countdown running out drops the hull.</summary>
    [Test]
    public async Task GraceExpiryEntersFalling()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);
        await FreezeCharge(pair, site.Centrifuge);

        // Shortened before arming, since GraceEnd is stamped from it.
        await server.WaitPost(() =>
            entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).GraceDuration = ShortGrace);

        await SetCharge(pair, site.Centrifuge, 0.5f);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).GraceRunning, Is.True,
                "Precondition: the grace countdown armed."));

        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Falling),
                    "The grace countdown ran out and the hull did not fall.");
                Assert.That(comp.GraceRunning, Is.False, "The fall left the grace countdown running.");
            }
        });

        // The hull is mid-transit, so it goes before the stack it was falling into.
        await server.WaitPost(() => entMan.DeleteEntity(site.Cracker));
        await server.WaitRunTicks(1);

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The stage walk driven by anchor events: pair, drill, switch-off, dissolve and leaving orbit.</summary>
    [Test]
    public async Task StateTransitionsFromAnchorEvents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var anchors = server.System<WFGravityAnchorSystem>();
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildCrackerInOrbit(pair);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.Surveying), "A hull on the orbit layer should be surveying."));

        await DeployPair(pair, site, 0f, 24f, false);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0]).Partner, Is.Not.Null,
                    "Precondition: the anchors paired.");
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                    Is.EqualTo(WFCrackState.AnchorsPlaced), "A fresh owned pair did not reach anchors-placed.");
            }
        });

        await server.WaitPost(() => anchors.CompleteDrill(
            (site.Anchors[0], entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0]))));

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.AnchorsPlaced),
                "One finished drill promoted the hull on its own; both halves have to be locked."));

        await server.WaitPost(() => anchors.CompleteDrill(
            (site.Anchors[1], entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[1]))));

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.AnchorsLocked), "Both drills finished and the pair is not targetable."));

        // Switched off, not unpaired, so the hull drops one stage.
        await server.WaitPost(() => anchors.ForceSwitchOff(
            (site.Anchors[0], entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0]))));

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0]).Partner, Is.Not.Null,
                    "Precondition: switching off does not dissolve the pair.");
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                    Is.EqualTo(WFCrackState.AnchorsPlaced),
                    "An unlocked half left the hull claiming a targetable pair.");
            }
        });

        await server.WaitPost(() => transform.Unanchor(
            site.Anchors[0], entMan.GetComponent<TransformComponent>(site.Anchors[0])));

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[1]).Partner, Is.Null,
                    "Precondition: the pair dissolved.");
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                    Is.EqualTo(WFCrackState.Surveying), "A hull with no pair left is not surveying.");
            }
        });

        var map = await pair.CreateTestMap();

        await server.WaitPost(() => transform.SetCoordinates(
            site.Cracker, new EntityCoordinates(map.MapUid, new Vector2(400f, 400f))));

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.Idle), "A hull off the orbit layer is not idle."));

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Structural damage that the modifier sets cannot soak.</summary>
    private static DamageSpecifier Damage(IPrototypeManager proto, float amount)
    {
        return new DamageSpecifier(proto.Index<DamageTypePrototype>(Blunt), FixedPoint2.New(amount));
    }
}
