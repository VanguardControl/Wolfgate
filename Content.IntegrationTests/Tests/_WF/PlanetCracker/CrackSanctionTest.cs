#nullable enable
using Content.IntegrationTests.Pair;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server._WF.PlanetCracker.Sanction;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.GameObjects;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// F9's sanction notices: the one a hull earns when it starts cutting a world with no licence on file, and the one the
/// chunk earns when it finally comes clear. Nothing in Content.IntegrationTests captures chat, so every assertion here
/// runs against the system's own post-dispatch counters (which prove the DispatchGlobalAnnouncement call was made) and
/// against its public BuildNotice, which is the exact builder production dispatches. The counters are per-server and
/// PoolManager reuses servers between tests, so every count is read as a DELTA against a baseline captured immediately
/// before the acting call.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCrackSanctionSystem))]
public sealed class CrackSanctionTest
{
    /// <summary>
    /// The unsanctioned test surface, owned by SurveyConsoleTest. [TestPrototypes] fields are discovered assembly-wide
    /// and loaded into every pair, so F9 ships no prototype of its own rather than adding a second surface that would
    /// have to dodge the planet types DeepVeinTest and SurveyConsoleTest already claim.
    /// </summary>
    private const string BareSurface = "WFTestBareSurface";

    /// <summary>A world name no prototype would produce, so finding it in a notice proves the builder read this body.</summary>
    private const string PlanetName = "Qorvath Reach";

    /// <summary>The same for the hull, and deliberately not a substring of the planet name.</summary>
    private const string ShipName = "ISV Loudmouth";

    /// <summary>
    /// An unsanctioned body announces exactly once as the cut begins, latches, absorbs a re-entry into Cracking that
    /// skipped AnchorsLocked, and announces again on a real AnchorsLocked -> Cracking re-begin.
    /// </summary>
    [Test]
    public async Task UnsanctionedCrackAnnouncesOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var sanction = server.System<WFCrackSanctionSystem>();
        var crackers = server.System<WFCrackerSystem>();

        var site = await BuildReadyToCut(pair);

        // Rewrites only the BODY copy of Sanctioned, which is the copy F9 reads (F2 D-J). The frozen
        // WFPlanetNetworkComponent.Surface stays Asclepiu, so terrain, veins and the built stack are untouched and
        // nothing needs rebuilding.
        await server.WaitPost(() => ApplySurfaceTo(pair, site.Planet, BareSurface));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFSectorPlanetComponent>(site.Planet).Sanctioned, Is.False,
                $"Precondition: {BareSurface} no longer flips the body unsanctioned, so this test proves nothing."));

        var before = sanction.CrackingNotices;

        await BeginCut(pair, site);

        await server.WaitAssertion(() =>
        {
            var latched = entMan.TryGetComponent<WFCrackNoticeComponent>(site.Planet, out var notice);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sanction.CrackingNotices - before, Is.EqualTo(1),
                    "The begin notice did not go out exactly once for an unsanctioned world.");
                Assert.That(latched, Is.True, "No latch was placed on the body, so the notice cannot be deduplicated.");
                Assert.That(notice?.AnnouncedCracking, Is.True, "The begin latch was not set after the notice went out.");
            }
        });

        // The admin seam: a pull back to AnchorsPlaced and straight into Cracking skips AnchorsLocked, so the latch is
        // never re-armed and the second entry is silent.
        await server.WaitPost(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            crackers.SetState(cracker, WFCrackState.AnchorsPlaced);
            crackers.SetState(cracker, WFCrackState.Cracking);
        });

        await server.WaitAssertion(() =>
            Assert.That(sanction.CrackingNotices - before, Is.EqualTo(1),
                "A re-entry into Cracking that skipped AnchorsLocked raised a second notice; the latch is not holding."));

        // The abort-then-re-begin shape: FinishAbort drops the hull back and the crew re-drills, so the next begin
        // crosses AnchorsLocked. D7 wants that retry to summon players again, which a permanent latch would silence.
        await server.WaitPost(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            crackers.SetState(cracker, WFCrackState.AnchorsLocked);
            crackers.SetState(cracker, WFCrackState.Cracking);
        });

        await server.WaitAssertion(() =>
            Assert.That(sanction.CrackingNotices - before, Is.EqualTo(2),
                "An AnchorsLocked -> Cracking re-begin was silent; the latch never re-armed, so a retry summons nobody."));

        await Cleanup(pair, site);
    }

    /// <summary>A licensed world is cut in silence: no notice, and no latch component left on the body.</summary>
    [Test]
    public async Task SanctionedCrackIsSilent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var sanction = server.System<WFCrackSanctionSystem>();

        var site = await BuildReadyToCut(pair);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFSectorPlanetComponent>(site.Planet).Sanctioned, Is.True,
                "Precondition: the stock Asclepiu body is not sanctioned, so silence here would prove nothing."));

        var before = sanction.CrackingNotices;

        await BeginCut(pair, site);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sanction.CrackingNotices - before, Is.Zero,
                    "A sanctioned world raised a begin notice; every licensed cut would spam the sector.");
                Assert.That(entMan.HasComponent<WFCrackNoticeComponent>(site.Planet), Is.False,
                    "A latch was placed on a sanctioned world, so the check runs after the component is ensured.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Both notices name the world and the hull by their own entity names, with no placeholder left unresolved.</summary>
    [Test]
    public async Task NoticesNameThePlanetAndTheShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var sanction = server.System<WFCrackSanctionSystem>();
        var metaData = server.System<MetaDataSystem>();

        var site = await BuildReadyToCut(pair);

        await server.WaitPost(() =>
        {
            metaData.SetEntityName(site.Planet, PlanetName);
            metaData.SetEntityName(site.Cracker, ShipName);
        });

        await server.WaitAssertion(() =>
        {
            var cracking = sanction.BuildNotice("wf-crack-sanction-cracking", site.Planet, site.Cracker);
            var extracted = sanction.BuildNotice("wf-crack-sanction-extracted", site.Planet, site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cracking, Does.Contain(PlanetName), $"The begin notice does not name the world: {cracking}");
                Assert.That(cracking, Does.Contain(ShipName), $"The begin notice does not name the hull: {cracking}");
                Assert.That(cracking, Does.Not.Contain("{"), $"The begin notice left a placeholder unresolved: {cracking}");
                Assert.That(extracted, Does.Contain(PlanetName), $"The extraction notice does not name the world: {extracted}");
                Assert.That(extracted, Does.Contain(ShipName), $"The extraction notice does not name the hull: {extracted}");
                Assert.That(extracted, Does.Not.Contain("{"), $"The extraction notice left a placeholder unresolved: {extracted}");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>A completed cut on an unsanctioned world raises the second, sound-free notice on top of the begin one.</summary>
    [Test]
    public async Task ExtractionAnnouncesForAnUnsanctionedPlanet()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var sanction = server.System<WFCrackSanctionSystem>();

        var site = await BuildReadyToExtract(pair);

        // Body copy only, as above: the network keeps the Asclepiu surface it was built from.
        await server.WaitPost(() => ApplySurfaceTo(pair, site.Planet, BareSurface));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFSectorPlanetComponent>(site.Planet).Sanctioned, Is.False,
                $"Precondition: {BareSurface} no longer flips the body unsanctioned, so this test proves nothing."));

        var beforeCracking = sanction.CrackingNotices;
        var beforeExtraction = sanction.ExtractionNotices;

        await BeginCut(pair, site);
        await CompleteCut(pair, site);

        await server.WaitAssertion(() =>
        {
            var latched = entMan.TryGetComponent<WFCrackNoticeComponent>(site.Planet, out var notice);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sanction.ExtractionNotices - beforeExtraction, Is.EqualTo(1),
                    "The extraction notice did not go out exactly once when the chunk came clear.");
                Assert.That(sanction.CrackingNotices - beforeCracking, Is.EqualTo(1),
                    "The begin notice count changed by something other than the one cut this test ran.");
                Assert.That(latched, Is.True, "No latch was placed on the body by the extraction notice.");
                Assert.That(notice?.AnnouncedExtraction, Is.True, "The extraction latch was not set after the notice went out.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The chunk goes before the stack it is hanging over; a test that never cut one pays a no-op FindChunk.</summary>
    private static async Task Cleanup(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var chunk = FindChunk(entMan);

            if (chunk != EntityUid.Invalid)
                entMan.DeleteEntity(chunk);
        });

        await server.WaitRunTicks(1);

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }
}
