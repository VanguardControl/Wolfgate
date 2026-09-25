#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.PlanetCracker.Survey;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Singularity.Components;
using Content.Shared.Tag;
using Content.Shared.Timing;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>The handheld surveyor: reveal radius and owner, ground gate, cooldown, examine and pulse.</summary>
[TestFixture]
[TestOf(typeof(WFSurveyorSystem))]
public sealed class SurveyorTest
{
    /// <summary>The handheld itself.</summary>
    private const string SurveyorProto = "WFSurveyor";

    /// <summary>The one-shot ground pulse the scan spawns.</summary>
    private const string PulseProto = "WFEffectSurveyPulse";

    /// <summary>The pulse's lifetime in seconds.</summary>
    private const float PulseLifetime = 0.48f;

    /// <summary>The engine tag that makes TryStartDoAfter raise the completion in the same call.</summary>
    private const string InstantTag = "InstantDoAfters";

    /// <summary>Inside WFSurveyorComponent's 12-tile pulse radius.</summary>
    private const float NearVein = 6.5f;

    /// <summary>Well outside it, and outside any rounding of it.</summary>
    private const float FarVein = 20.5f;

    /// <summary>A pulse reveals the vein inside its radius and not the one outside it.</summary>
    [Test]
    public async Task RevealsInsideTheRadiusOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var layers = await DeepVeinTest.BuildTestGround(pair);
        var ground = layers[0];

        var near = await LayAndSpawnVein(pair, ground, NearVein);
        var far = await LayAndSpawnVein(pair, ground, FarVein);
        var (user, surveyor) = await SpawnScanner(pair, ground, new Vector2(0.5f, 0.5f));

        await Scan(pair, user, surveyor);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent(user, out WFSurveyedComponent? surveyed), Is.True,
                "The scan wrote no surveyed component at all, so nothing was revealed.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(surveyed!.Revealed, Does.Contain(entMan.GetNetEntity(near)),
                    "A vein six tiles from the pulse was not revealed.");
                Assert.That(surveyed.Revealed, Does.Not.Contain(entMan.GetNetEntity(far)),
                    "A vein twenty tiles from a twelve-tile pulse was revealed anyway.");
                Assert.That(surveyed.LastPulseRadius, Is.EqualTo(12f).Within(0.001f),
                    "The pulse did not record the radius the overlay fades over.");
                Assert.That(surveyed.LastPulse, Is.Not.Null, "The pulse recorded no origin for the overlay.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The reveal is recorded on the scanning player, so a bystander learns nothing.</summary>
    [Test]
    public async Task RevealIsPerPlayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var layers = await DeepVeinTest.BuildTestGround(pair);
        var ground = layers[0];

        var vein = await LayAndSpawnVein(pair, ground, NearVein);
        var (user, surveyor) = await SpawnScanner(pair, ground, new Vector2(0.5f, 0.5f));
        var bystander = await SpawnUser(pair, ground, new Vector2(1.5f, 0.5f));

        await Scan(pair, user, surveyor);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFSurveyedComponent>(user).Revealed,
                    Does.Contain(entMan.GetNetEntity(vein)), "Precondition: the scanner revealed the vein.");
                Assert.That(entMan.TryGetComponent(bystander, out WFSurveyedComponent? other) && other!.Revealed.Count > 0,
                    Is.False, "Someone standing beside the scanner was handed the reveal.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A scan on a hull deck is refused; only planet ground qualifies.</summary>
    [Test]
    public async Task ScanRefusedOffTheGroundLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);

        var map = await pair.CreateTestMap();
        var hull = await BuildCracker(pair, map.MapId);

        var (user, surveyor) = await SpawnScanner(pair, hull, new Vector2(0.5f, 0.5f));

        await Scan(pair, user, surveyor);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFSurveyedComponent>(user), Is.False,
                    "A scan on a hull deck wrote a surveyed component, so the ground gate is not running.");
                Assert.That(FindPulse(entMan), Is.EqualTo(EntityUid.Invalid),
                    "A refused scan still spawned its ground pulse.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The surveyor also works on an extracted chunk, whose veins ride up with it.</summary>
    [Test]
    public async Task ScansTheExtractedChunk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildReadyToExtract(pair);

        // The pair at x 0 and 16 centres the cut circle on (8.5, 0.5).
        var centre = new Vector2(8.5f, 0.5f);
        await DeepVeinTest.LayTile(pair, site.Ground, new Vector2i(8, 0), DeepVeinTest.GrassTile);
        var vein = await DeepVeinTest.SpawnVein(pair, site.Ground, centre);

        await server.WaitAssertion(() =>
            Assert.That(entMan.EntityExists(vein), Is.True, "Precondition: the vein survived its MapInit on the ground."));

        await BeginCut(pair, site);
        await CompleteCut(pair, site);

        var chunk = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(vein), Is.True, "Precondition: the vein survived the cut.");
            chunk = entMan.GetComponent<TransformComponent>(vein).GridUid ?? EntityUid.Invalid;
            Assert.That(entMan.HasComponent<WFPlanetChunkComponent>(chunk), Is.True,
                "Precondition: the vein rode up onto the chunk.");
        });

        // The chunk is world-aligned at the ground's own indices, so ground XY is chunk-local XY.
        var (user, surveyor) = await SpawnScanner(pair, chunk, centre + new Vector2(2f, 0f));

        await Scan(pair, user, surveyor);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent(user, out WFSurveyedComponent? surveyed), Is.True,
                "The scan on the chunk was refused outright.");
            Assert.That(surveyed!.Revealed, Does.Contain(entMan.GetNetEntity(vein)),
                "The vein riding the chunk was not revealed.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The scan cooldown comes from the prototype's UseDelay.</summary>
    [Test]
    public async Task CooldownIsEnforcedByUseDelay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var layers = await DeepVeinTest.BuildTestGround(pair);
        var ground = layers[0];

        var vein = await LayAndSpawnVein(pair, ground, NearVein);
        var (user, surveyor) = await SpawnScanner(pair, ground, new Vector2(0.5f, 0.5f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<UseDelayComponent>(surveyor), Is.True,
                "The surveyor carries no UseDelay, so there is no cooldown on the scan at all."));

        await Scan(pair, user, surveyor);

        await server.WaitAssertion(() =>
        {
            var delays = server.System<UseDelaySystem>();

            Assert.That(entMan.TryGetComponent(surveyor, out UseDelayComponent? delay), Is.True);
            Assert.That(delays.IsDelayed((surveyor, delay!)), Is.True,
                "The first scan did not start the item's delay, so a second one would be free.");

            // Wipe the first scan's reveal, so only an accepted second scan restores it.
            entMan.GetComponent<WFSurveyedComponent>(user).Revealed.Clear();
        });

        await Scan(pair, user, surveyor);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFSurveyedComponent>(user).Revealed,
                Does.Not.Contain(entMan.GetNetEntity(vein)),
                "A second scan inside the delay ran anyway."));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Examine is silent before a pulse, then names the ore and a band word, never the tonnage.</summary>
    [Test]
    public async Task ExamineIsGatedOnTheReveal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var layers = await DeepVeinTest.BuildTestGround(pair);
        var ground = layers[0];

        var vein = await LayAndSpawnVein(pair, ground, NearVein);
        var (user, surveyor) = await SpawnScanner(pair, ground, new Vector2(0.5f, 0.5f));

        var unknown = string.Empty;

        await server.WaitAssertion(() =>
        {
            unknown = Examine(entMan, vein, user);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(unknown, Does.Contain(Loc.GetString("wf-vein-examine-unknown")),
                    "An unrevealed vein does not push the unknown line.");
                Assert.That(unknown, Does.Not.Contain(Loc.GetString("wf-vein-examine-yield", ("band", string.Empty)).Trim()),
                    "An unrevealed vein leaked its yield line.");
            }
        });

        await Scan(pair, user, surveyor);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);
            var ore = server.System<SharedWFSurveySystem>().GetOreName(comp.Ore);
            var revealed = Examine(entMan, vein, user);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(revealed, Does.Not.Contain(Loc.GetString("wf-vein-examine-unknown")),
                    "A revealed vein still pushes the unknown line.");
                Assert.That(revealed, Does.Contain(ore), "A revealed vein does not name its ore.");
                Assert.That(revealed, Does.Contain(Loc.GetString(
                        SharedWFSurveySystem.YieldBandKey(comp.TotalYield, comp.YieldRange))),
                    "A revealed vein does not name the band its yield falls in.");
                Assert.That(revealed, Does.Not.Contain(comp.TotalYield.ToString()),
                    "The examine printed the vein's exact yield.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The pulse carries a singularity distortion that reaches the client, and despawns on time.</summary>
    [Test]
    public async Task PulseEffectDistortsAndDespawnsOnTime()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;

        var layers = await DeepVeinTest.BuildTestGround(pair);
        var ground = layers[0];

        // The scanner is the attached player, so the effect is in its client's PVS.
        var user = await AttachViewer(pair, ground, new Vector2(0.5f, 0.5f));
        var surveyor = await ArmScanner(pair, user);

        await Scan(pair, user, surveyor);

        var pulse = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            pulse = FindPulse(entMan);

            Assert.That(pulse, Is.Not.EqualTo(EntityUid.Invalid), "The scan spawned no ground pulse.");

            // Read off the prototype, since the live entity's Lifetime counts down.
            var proto = server.ResolveDependency<IPrototypeManager>().Index<EntityPrototype>(PulseProto);

            Assert.That(proto.TryGetComponent<TimedDespawnComponent>(out var despawn,
                    server.ResolveDependency<IComponentFactory>()), Is.True,
                "The pulse prototype carries no TimedDespawn, so it would hang on the ground forever.");
            Assert.That(despawn!.Lifetime, Is.EqualTo(PulseLifetime).Within(0.001f),
                "The pulse's lifetime moved off the half second the scan is pitched at.");
            Assert.That(entMan.GetComponent<TimedDespawnComponent>(pulse).Lifetime,
                Is.GreaterThan(0f).And.LessThanOrEqualTo(PulseLifetime),
                "The spawned pulse is not counting down from its prototype's lifetime.");
        });

        // Eight ticks is 0.27 s: long enough for PVS to deliver the effect, well short of its 0.48 s lifetime.
        await pair.RunTicksSync(8);

        await client.WaitAssertion(() =>
        {
            var uid = pair.ToClientUid(pulse);

            // The client overlay draws from this component.
            Assert.That(client.EntMan.TryGetComponent(uid, out SingularityDistortionComponent? distortion), Is.True,
                "The pulse never reached the client as a distortion, so nobody saw anything at all.");
            Assert.That(distortion!.Intensity, Is.GreaterThan(0f),
                "The pulse's distortion has no intensity, which draws exactly nothing.");
        });

        // Well past 0.48 s at the pooled tickrate.
        await pair.RunTicksSync(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.EntityExists(pulse), Is.False, "The pulse outlived its own TimedDespawn."));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Lays one grass tile at the given X on the ground layer's zero row and puts a vein on it.</summary>
    private static async Task<EntityUid> LayAndSpawnVein(TestPair pair, EntityUid ground, float x)
    {
        await DeepVeinTest.LayTile(pair, ground, new Vector2i((int) x, 0), DeepVeinTest.GrassTile);

        var vein = await DeepVeinTest.SpawnVein(pair, ground, new Vector2(x, 0.5f));

        await pair.Server.WaitAssertion(() =>
            Assert.That(pair.Server.EntMan.EntityExists(vein), Is.True,
                "Precondition: the hand-spawned vein survived its own MapInit."));

        return vein;
    }

    /// <summary>A plain mob on a grid, with no session behind it: the reveal lands on the entity either way.</summary>
    private static async Task<EntityUid> SpawnUser(TestPair pair, EntityUid grid, Vector2 pos)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var user = EntityUid.Invalid;

        await server.WaitPost(() => user = entMan.SpawnEntity(ViewerProto, new EntityCoordinates(grid, pos)));
        await server.WaitRunTicks(1);
        return user;
    }

    /// <summary>Gives a mob the surveyor in hand and the tag that collapses the scan DoAfter to one call.</summary>
    private static async Task<EntityUid> ArmScanner(TestPair pair, EntityUid user)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var surveyor = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            server.System<TagSystem>().AddTag(user, InstantTag);

            surveyor = entMan.SpawnEntity(SurveyorProto, entMan.GetComponent<TransformComponent>(user).Coordinates);

            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(user, surveyor), Is.True,
                "Precondition: the scanner picked the surveyor up.");
        });

        await server.WaitRunTicks(1);
        return surveyor;
    }

    /// <summary>A mob standing on a grid with a surveyor in hand.</summary>
    private static async Task<(EntityUid User, EntityUid Surveyor)> SpawnScanner(TestPair pair, EntityUid grid, Vector2 pos)
    {
        var user = await SpawnUser(pair, grid, pos);
        return (user, await ArmScanner(pair, user));
    }

    /// <summary>Uses the surveyor in hand through the real interaction path, cooldown gate and all.</summary>
    private static async Task Scan(TestPair pair, EntityUid user, EntityUid surveyor)
    {
        var server = pair.Server;

        await server.WaitPost(() => server.System<SharedInteractionSystem>().UseInHandInteraction(user, surveyor));
        await server.WaitRunTicks(1);
    }

    /// <summary>The plain text one examiner would see on a vein.</summary>
    private static string Examine(IEntityManager entMan, EntityUid vein, EntityUid examiner)
    {
        var ev = new ExaminedEvent(new FormattedMessage(), vein, examiner, true, false);
        entMan.EventBus.RaiseLocalEvent(vein, ev);

        return ev.GetTotalMessage().ToString();
    }

    /// <summary>The one ground pulse in the world, or Invalid.</summary>
    private static EntityUid FindPulse(IEntityManager entMan)
    {
        var query = entMan.AllEntityQueryEnumerator<TimedDespawnComponent, MetaDataComponent>();

        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == PulseProto)
                return uid;
        }

        return EntityUid.Invalid;
    }
}
