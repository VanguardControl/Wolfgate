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

/// <summary>
/// The handheld surveyor: what one pulse reveals and to whom, that the reveal is refused anywhere but a planet's
/// ground layer, that the cooldown is the item's own UseDelay rather than anything this feature wrote, that the vein
/// examine says nothing at all until the examiner has pulsed it, and that the pulse effect is the lensing ripple it
/// claims to be and despawns on its own schedule.
/// The scan is driven through the real interaction path - SharedInteractionSystem.UseInHandInteraction - with the
/// scanner carrying the engine's InstantDoAfters tag, so TryStartDoAfter raises the completion inside the same call
/// (Content.Shared/DoAfter/SharedDoAfterSystem.cs:259-263). That removes every source of flake a two-second wall-clock
/// DoAfter would add (BreakOnMove drift, BreakOnDamage from a test map's vacuum) while still running the whole chain:
/// the cooldown gate, ActionBlocker, the shared UseInHand handler and the server's completion handler.
/// Generation-free ground comes from DeepVeinTest's grass-only test surface, for the same reason it does there.
/// </summary>
[TestFixture]
[TestOf(typeof(WFSurveyorSystem))]
public sealed class SurveyorTest
{
    /// <summary>The handheld itself.</summary>
    private const string SurveyorProto = "WFSurveyor";

    /// <summary>The one-shot ground pulse the scan spawns.</summary>
    private const string PulseProto = "WFEffectSurveyPulse";

    /// <summary>The pulse's own half second; it draws no sprite, so nothing else pins this number.</summary>
    private const float PulseLifetime = 0.48f;

    /// <summary>The engine tag that makes TryStartDoAfter raise the completion in the same call.</summary>
    private const string InstantTag = "InstantDoAfters";

    /// <summary>Inside WFSurveyorComponent's 12-tile pulse radius.</summary>
    private const float NearVein = 6.5f;

    /// <summary>Well outside it, and outside any rounding of it.</summary>
    private const float FarVein = 20.5f;

    /// <summary>
    /// The whole point of a radius: what the pulse touched is revealed and what it did not is not. Both veins exist,
    /// are anchored and are on the same ground layer, so the only thing separating them is the distance.
    /// </summary>
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

    /// <summary>
    /// The reveal lives on the scanning player, never on the vein, so a second body standing on the same tile learns
    /// nothing. That is the whole reason WFSurveyedComponent is on the player: a flag on the vein would either leak to
    /// everyone who already has it in PVS or need per-viewer state filtering.
    /// </summary>
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

    /// <summary>
    /// Deep veins only exist on a planet's depth-zero biome grid, so the scan is gated on the same TryGetPlanetGround
    /// the gravity anchors deploy through. A hull deck is the case that matters: it is a grid, it is solid, and it is
    /// not ground.
    /// </summary>
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

    /// <summary>
    /// The disc cut out of the ground keeps its veins, so the surveyor keeps working on it wherever it hangs: the scan
    /// gate accepts a chunk grid as well as the ground layer. It used to refuse with "not on ground" in the berth.
    /// The vein is planted on the ground before the cut and rides up with the disc, as a biome-spawned one does.
    /// </summary>
    [Test]
    public async Task ScansTheExtractedChunk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildReadyToExtract(pair);

        // The ready site's pair sits at x 0 and 16 on the zero row, so the cut circle is centred on (8.5, 0.5).
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

    /// <summary>
    /// The cooldown is the prototype's UseDelay block and nothing else: SharedInteractionSystem refuses a use while
    /// IsDelayed (:1215) and resets the delay itself once the event comes back Handled (:1230). Nothing in F2 counts
    /// seconds, so this is what proves there is a cooldown at all.
    /// </summary>
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

            // Wipe what the first scan found: if the second one is refused, nothing puts it back.
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

    /// <summary>
    /// Examine is the payoff and the leak at once: before a pulse it says nothing, after one it names the ore and a
    /// BAND word. The exact tonnage never appears - that is F6's business and would turn one survey into a spreadsheet.
    /// </summary>
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

    /// <summary>
    /// The pulse has NO art: it is a SingularityDistortion the client's own singularity overlay lenses the screen
    /// with, so what has to hold is that the component is on it, that it reaches the client at all - the component's
    /// ComponentStartup is what takes the global PVS override that gets it there - and that the TimedDespawn still
    /// takes it away. A pulse that lost the component would be a completely invisible effect with nothing to say so.
    /// This lives here rather than in PlanetCrackerPrototypeTest because it despawns faster than that file's own
    /// 15-tick sprite sweep, and because it belongs on planet ground rather than on a FloorSteel test grid.
    /// </summary>
    [Test]
    public async Task PulseEffectDistortsAndDespawnsOnTime()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;

        var layers = await DeepVeinTest.BuildTestGround(pair);
        var ground = layers[0];

        // The scanner IS the attached player, so the effect it spawns is inside its own client's PVS.
        var user = await AttachViewer(pair, ground, new Vector2(0.5f, 0.5f));
        var surveyor = await ArmScanner(pair, user);

        await Scan(pair, user, surveyor);

        var pulse = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            pulse = FindPulse(entMan);

            Assert.That(pulse, Is.Not.EqualTo(EntityUid.Invalid), "The scan spawned no ground pulse.");

            // The shipped number is read off the prototype: TimedDespawnSystem counts Lifetime DOWN on the live
            // entity, so a spawned one is already short by however many ticks have gone by.
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

            // SingularityOverlay.BeforeDraw walks SingularityDistortionComponent on the CLIENT, so the component
            // reaching the client is the whole of "somebody saw it".
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
