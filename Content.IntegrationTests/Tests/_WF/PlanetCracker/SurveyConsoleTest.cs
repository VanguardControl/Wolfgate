#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WF.Stylesheets;
using Content.IntegrationTests.Pair;
using Content.Server._FarHorizons.StarSystem;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Server._WF.PlanetCracker.Survey;
using Content.Server.Shuttles.Components;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared._WF.PlanetCracker.Survey.BUI;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;
using ClientSurveyRow = Content.Client._WF.PlanetCracker.Survey.WFSurveyPlanetRow;
using ClientSurveyWindow = Content.Client._WF.PlanetCracker.Survey.WFSurveyConsoleWindow;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// The sector survey console's state object, read straight off the server's own builder with no window standing up -
/// the CrackConsoleTest recipe. What it has to get right: one row per star-system body whether or not that body has a
/// Wolfgate surface, a distance that is a raw Vector2 length or nothing at all, the sanctioned and cracked flags read
/// off the body rather than off a prototype, a vein RATING with no contents behind it, and the body's own FTL beacon
/// name as the "go here".
/// </summary>
[TestFixture]
[TestOf(typeof(WFSurveyConsoleSystem))]
public sealed class SurveyConsoleTest
{
    /// <summary>
    /// A second surface, so the console has a world that is unsanctioned and rolls no veins to show beside Asclepiu.
    /// The test surface is explicitly applied to Thrascias after registry initialization; it does not replace
    /// the registry entry or change the shipped planet definition.
    /// </summary>
    [TestPrototypes]
    public const string Prototypes = @"
- type: wfPlanetSurface
  id: WFTestBareSurface
  planetType: PlanetThrascias
  ground: WFAsclepiuSurface
  airLayers: 1
  cloudLayer: false
  buildAtRoundStart: false
  sanctioned: false
";

    /// <summary>The star system every shipped map runs in, and the one the console enumerates.</summary>
    private const string System = "SystemKyphrus";

    /// <summary>The console prototype itself.</summary>
    private const string ConsoleProto = "WFSectorSurveyConsole";

    /// <summary>The one shipped crackable world.</summary>
    private const string AsclepiuType = "PlanetAsclepiu";

    /// <summary>The body the test-only bare surface is stamped onto.</summary>
    private const string BareType = "PlanetThrascias";

    /// <summary>The unsanctioned, veinless surface declared above. Plain string: the linter skips test prototypes.</summary>
    private const string BareSurface = "WFTestBareSurface";

    /// <summary>The shipped vein table Asclepiu rolls from; a const rather than a literal, which Index forbids.</summary>
    private const string AsclepiuVeins = "WFVeinTableAsclepiu";

    /// <summary>Well away from the test map's own grid, so the console's own world position is what gets measured.</summary>
    private static readonly Vector2 ConsolePos = new(100f, 0f);

    /// <summary>
    /// Every body in the system gets a row, not just the registered ones: WFSurfaceAsclepiu is the only shipped
    /// surface against five bodies, so a registry-only list would be one row on a live server and zero on a stock one.
    /// </summary>
    [Test]
    public async Task StateListsEveryStarSystemBody()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        var console = await BuildSector(pair, true);

        await server.WaitAssertion(() =>
        {
            var state = server.System<WFSurveyConsoleSystem>().BuildState(console);
            var system = proto.Index<StarSystemPrototype>(System);
            var expected = system.Planets.Select(entry => proto.Index(entry.Planet).Name).ToList();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(state.SystemName, Is.EqualTo(System), "The state names the wrong star system.");
                Assert.That(state.Planets, Has.Count.EqualTo(system.Planets.Count),
                    "The console did not list one row per star-system entry.");
                Assert.That(state.Planets.Select(row => row.Name), Is.EquivalentTo(expected),
                    "The rows are not named after the bodies' own PlanetTypePrototype names.");
                Assert.That(Row(state, AsclepiuType, proto).HasSurface, Is.True,
                    "The one shipped crackable world is not flagged as having crackable ground.");
                Assert.That(Row(state, "PlanetFervidus", proto).HasSurface, Is.True,
                    "A shipped planet surface is missing from the survey.");
                Assert.That(Row(state, "PlanetMerak", proto).HasSurface, Is.True,
                    "A shipped planet surface is missing from the survey.");
                Assert.That(Row(state, "PlanetAerumna", proto).HasSurface, Is.True,
                    "A shipped planet surface is missing from the survey.");
                Assert.That(Row(state, "PlanetCarcinoma", proto).Sanctioned, Is.False);
            }
        });

        await TeardownSector(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The distance is a raw Vector2 length in the sector frame and nothing else - never MapCoordinates.InRange, which
    /// returns false across MapIds before it does any maths. A console that cannot resolve its own sector position
    /// reports that, rather than measuring from the origin and handing the pilot a number that means nothing.
    /// </summary>
    [Test]
    public async Task DistanceIsRawVectorAndKnownOnlyWhenResolvable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();

        var console = await BuildSector(pair, true);
        var elsewhere = await pair.CreateTestMap();
        var stranded = EntityUid.Invalid;

        // Off the test map's own grid: a computer ships anchored, and anchoring onto a grid it is not parented to is
        // an engine-level error rather than a survey console problem.
        await server.WaitPost(() =>
            stranded = entMan.SpawnEntity(ConsoleProto, new EntityCoordinates(elsewhere.MapUid, ConsolePos)));

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var consoles = server.System<WFSurveyConsoleSystem>();
            var state = consoles.BuildState(console);
            var system = proto.Index<StarSystemPrototype>(System);
            var entry = system.Planets.First(planet => planet.Planet == AsclepiuType);

            // The same expression SharedStarSystemMapSystem and WFPlanetRegistrySystem both use, bit for bit.
            var pos = new Vector2(MathF.Cos(entry.Angle), MathF.Sin(entry.Angle)) * entry.Distance;
            var row = Row(state, AsclepiuType, proto);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(state.ConsolePositionKnown, Is.True,
                    "A console sitting on the sector map could not resolve its own position.");
                Assert.That(row.DistanceKnown, Is.True, "A resolvable console still reports its distances as unknown.");
                Assert.That(row.SectorPos.X, Is.EqualTo(pos.X).Within(0.01f), "The row's sector position drifted.");
                Assert.That(row.SectorPos.Y, Is.EqualTo(pos.Y).Within(0.01f), "The row's sector position drifted.");
                Assert.That(row.Distance, Is.EqualTo((pos - ConsolePos).Length()).Within(0.01f),
                    "The row's distance is not the raw vector length from the console to the body.");
                Assert.That(state.Planets.Select(item => item.Distance), Is.Ordered,
                    "A console that knows where it is does not sort its rows by distance.");
            }

            var lost = consoles.BuildState(stranded);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(lost.ConsolePositionKnown, Is.False,
                    "A console on a map with no sector frame claimed to know where it was.");
                Assert.That(lost.Planets, Is.Not.Empty, "The stranded console listed no bodies at all.");
                Assert.That(lost.Planets.All(item => !item.DistanceKnown), Is.True,
                    "A console that cannot place itself still reported a distance the window would draw.");
                Assert.That(lost.Planets.Select(item => item.Name).ToList(),
                    Is.EqualTo(lost.Planets.Select(item => item.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList()),
                    "A console that cannot place itself does not fall back to sorting by name.");
            }
        });

        await TeardownSector(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Sanctioned is mirrored onto the body by WFPlanetRegistrySystem.ApplySurface, the single writer, so a console
    /// reads it off one component that outlives the z-network instead of indexing a prototype. The test-only bare
    /// surface ships sanctioned: false precisely so this cannot pass on the component default.
    /// </summary>
    [Test]
    public async Task SanctionedMirrorsTheSurfacePrototype()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();

        var console = await BuildSector(pair, true);

        await server.WaitAssertion(() =>
        {
            var consoles = server.System<WFSurveyConsoleSystem>();
            var asclepiu = Body(entMan, proto, AsclepiuType);
            var bare = Body(entMan, proto, BareType);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFSectorPlanetComponent>(asclepiu).Sanctioned, Is.True,
                    "The shipped surface's sanctioned flag did not reach its body.");
                Assert.That(entMan.GetComponent<WFSectorPlanetComponent>(bare).Sanctioned, Is.False,
                    "An unsanctioned surface's flag did not reach its body, so ApplySurface is not mirroring it.");
            }

            var state = consoles.BuildState(console);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Row(state, AsclepiuType, proto).Sanctioned, Is.True, "The sanctioned row does not read sanctioned.");
                Assert.That(Row(state, BareType, proto).Sanctioned, Is.False, "The unsanctioned row does not read unsanctioned.");
            }

            // The row follows the COMPONENT, not the prototype: F9 has to be able to flip a world mid-round.
            entMan.GetComponent<WFSectorPlanetComponent>(asclepiu).Sanctioned = false;

            Assert.That(consoles.BuildState(console).Planets.First(row => row.HasSurface && row.Name == proto.Index<PlanetTypePrototype>(AsclepiuType).Name).Sanctioned,
                Is.False, "Flipping the body's own flag did not move the row.");
        });

        await TeardownSector(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>Cracked is F5's flag on the same body, and the console is the one thing that enumerates it.</summary>
    [Test]
    public async Task CrackedIsReadFromTheSectorBody()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();

        var console = await BuildSector(pair, true);

        await server.WaitAssertion(() =>
        {
            var consoles = server.System<WFSurveyConsoleSystem>();
            var body = Body(entMan, proto, AsclepiuType);

            Assert.That(Row(consoles.BuildState(console), AsclepiuType, proto).Cracked, Is.False,
                "An untouched world already reads as cracked.");

            entMan.GetComponent<WFSectorPlanetComponent>(body).Cracked = true;

            Assert.That(Row(consoles.BuildState(console), AsclepiuType, proto).Cracked, Is.True,
                "A cracked world does not read as cracked.");
        });

        await TeardownSector(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// THE WHOLE "GO HERE" CONTRACT. F2 spawns and renames nothing to give the pilot a destination: every sector body
    /// already ships `- type: FTLBeacon` (star_system.yml:20) and StarSystemMapSystem renames each spawn to the
    /// planet's name (:60-65), which is exactly the string ShuttleConsoleSystem.GetBeacons puts in the destination tree
    /// (ShuttleConsoleSystem.FTL.cs:99-108). If either of those ever stops being true, this is the alarm.
    /// It also pins the name itself, because WFPlanetRegistrySystem.TryGetPlanetByName still resolves bodies by it.
    /// </summary>
    [Test]
    public async Task RowReportsTheBodysOwnFtlBeacon()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();

        var console = await BuildSector(pair, true);

        await server.WaitAssertion(() =>
        {
            var state = server.System<WFSurveyConsoleSystem>().BuildState(console);
            var body = Body(entMan, proto, AsclepiuType);
            var name = entMan.GetComponent<MetaDataComponent>(body).EntityName;
            var row = Row(state, AsclepiuType, proto);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<FTLBeaconComponent>(body), Is.True,
                    "The sector body carries no FTLBeacon, so nothing the console names is reachable by FTL.");
                Assert.That(name, Is.EqualTo(proto.Index<PlanetTypePrototype>(AsclepiuType).Name),
                    "The sector body was renamed, so TryGetPlanetByName no longer resolves it.");
                Assert.That(row.HasBeacon, Is.True, "The row does not report the body's beacon.");
                Assert.That(row.Beacon, Is.EqualTo(name), "The row's destination is not the body's own name.");
                Assert.That(row.Planet, Is.EqualTo(entMan.GetNetEntity(body)), "The row does not point at the body.");
            }

            // What the pilot's own destination list is built from: every beacon-carrying body, by its MetaData name.
            var beacons = new List<string>();
            var query = entMan.AllEntityQueryEnumerator<FTLBeaconComponent, MetaDataComponent>();

            while (query.MoveNext(out _, out _, out var meta))
            {
                beacons.Add(meta.EntityName);
            }

            Assert.That(beacons, Does.Contain(row.Beacon),
                "The name the row tells the pilot to look for is not in the beacon list the shuttle console builds.");
        });

        await TeardownSector(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Design section 4's "without revealing exact contents": the RATING travels and nothing behind it does. Sanctioned
    /// and cracked are explicitly NOT secrets - WFSectorPlanetComponent is networked and every sector body carries a
    /// PVS global override (StarSystemMapSystem.cs:65) - so the derivation is the only thing that stays server-side.
    /// </summary>
    [Test]
    public async Task RatingTravelsAndContentsDoNot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        var console = await BuildSector(pair, true);

        await server.WaitAssertion(() =>
        {
            var state = server.System<WFSurveyConsoleSystem>().BuildState(console);
            var table = proto.Index<WFVeinTablePrototype>(AsclepiuVeins);
            var bands = proto.Index<WFVeinRatingBandsPrototype>(SharedWFSurveySystem.DefaultBands);
            var rated = Row(state, AsclepiuType, proto);
            var bare = Row(state, BareType, proto);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(rated.VeinsKnown, Is.True, "A world with a vein table carries no rating.");
                Assert.That(rated.Veins, Is.EqualTo(WFVeinRating.Fair),
                    "WFVeinTableAsclepiu no longer rates Fair against the shipped bands.");
                Assert.That(rated.Veins, Is.EqualTo(SharedWFSurveySystem.Rate(SharedWFSurveySystem.Score(table, true), bands)),
                    "The row's rating is not the shared maths' answer for this table.");
                Assert.That(bare.HasSurface, Is.True, "Precondition: the bare world is registered.");
                Assert.That(bare.VeinsKnown, Is.False, "A world with no vein table still claims a rating.");
                Assert.That(bare.Veins, Is.EqualTo(WFVeinRating.Poor), "An unrated world does not fall back to the default.");
            }

            // Structural: nothing on the wire is even SHAPED like an ore, a weight or a yield.
            var members = typeof(WFSurveyPlanetRow).GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Concat(typeof(WFSurveyConsoleState).GetMembers(BindingFlags.Public | BindingFlags.Instance))
                .Select(member => member.Name)
                .ToList();

            using (Assert.EnterMultipleScope())
            {
                foreach (var banned in new[] { "Ore", "Yield", "Weight" })
                {
                    Assert.That(members.Any(name => name.Contains(banned, StringComparison.OrdinalIgnoreCase)), Is.False,
                        $"The console state gained a member named for {banned}; the rating is all that may travel.");
                }
            }

            // And nothing in the values either: a row prints every member it has.
            var printed = string.Join("\n", state.Planets.Select(row => row.ToString()));

            using (Assert.EnterMultipleScope())
            {
                foreach (var ore in table.Ores.Keys)
                {
                    Assert.That(printed, Does.Not.Contain(ore.Id), $"The state leaked the ore id {ore.Id}.");
                }
            }
        });

        await TeardownSector(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The default server: wf.planet_networks is SERVERONLY and defaults false, so registration returns before it ever
    /// adds a component. The star-system enumeration is what keeps the window from being empty, and every row has to
    /// say "no crackable ground" rather than throw.
    /// </summary>
    [Test]
    public async Task EmptyRegistryProducesUnsurfacedRows()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var console = await BuildSector(pair, false);

        await server.WaitAssertion(() =>
        {
            var state = server.System<WFSurveyConsoleSystem>().BuildState(console);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(state.Planets, Is.Not.Empty, "With the feature off the console listed nothing at all.");
                Assert.That(state.Planets.All(row => !row.HasSurface), Is.True,
                    "A body was registered with wf.planet_networks off.");
                Assert.That(state.Planets.All(row => !row.VeinsKnown), Is.True,
                    "An unregistered body carries a vein rating.");
                Assert.That(state.Planets.All(row => row.Planet == null), Is.True,
                    "An unregistered row points at a body entity.");
                Assert.That(state.Planets.All(row => !string.IsNullOrEmpty(row.Beacon)), Is.True,
                    "An unregistered row names no destination at all.");
            }
        });

        await TeardownSector(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The window itself, driven headlessly: construction, three pushes of a state that grows, shrinks and empties,
    /// and a Measure/Arrange pass after each. A real DrawingHandleScreen cannot be obtained in a pooled pair, so this
    /// asserts on layout and on the row list instead.
    /// What it pins: the rows are REUSED rather than rebuilt - the server pushes once a second, so a window that drops
    /// every control per push reallocates the list sixty times a minute and throws the scroll position away - and the
    /// window never widens past its own SetSize no matter how long a body name is, which is what a clipped, fixed-width
    /// name column buys.
    /// </summary>
    [Test]
    public async Task WindowTakesEveryStateShapeWithoutThrowing()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Client.WaitPost(() =>
        {
            var window = new ClientSurveyWindow();

            Assert.DoesNotThrow(() => window.UpdateState(SyntheticState(3)),
                "The window should take a three-row state without throwing.");

            Layout(window);

            var rows = window.Rows.ToList();

            Assert.That(rows, Has.Count.EqualTo(3), "The window drew a different number of rows than the state had.");

            // Same identities again: the controls must be the same objects, not fresh ones.
            window.UpdateState(SyntheticState(3));
            Layout(window);

            Assert.That(window.Rows.ToList(), Is.EqualTo(rows), "A second push of the same shape rebuilt every row control.");

            window.UpdateState(SyntheticState(1));
            Layout(window);

            Assert.That(window.Rows, Has.Count.EqualTo(1), "The window kept rows the state no longer carries.");

            Assert.DoesNotThrow(() => window.UpdateState(new WFSurveyConsoleState()),
                "The window should take an empty state without throwing.");

            Layout(window);

            Assert.That(window.Rows, Is.Empty, "An empty state left rows on the window.");

            window.Dispose();
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A body name far longer than its column may not widen the row: the name cell clips, so the row's desired width
    /// is the fixed columns plus the name column's floor whatever the string is. Without the clip a long entity name
    /// pushes the table off the side of the window, which is the whole reason the column has a fixed width.
    /// </summary>
    [Test]
    public async Task LongBodyNameDoesNotWidenTheRow()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Client.WaitPost(() =>
        {
            var skin = WolfgateSkins.Futurist;
            var unbounded = new Vector2(float.PositiveInfinity, float.PositiveInfinity);

            // The window is only here for the width its own XAML asks for, so the ceiling cannot drift from it.
            var window = new ClientSurveyWindow();
            var shortRow = new ClientSurveyRow();
            var longRow = new ClientSurveyRow();

            shortRow.SetData(SyntheticState(1).Planets[0], skin);
            longRow.SetData(SyntheticState(1, new string('M', 400)).Planets[0], skin);

            shortRow.Measure(unbounded);
            longRow.Measure(unbounded);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(longRow.DesiredSize.X, Is.EqualTo(shortRow.DesiredSize.X).Within(0.01f),
                    "A four-hundred character body name changed the row's width; the name cell is not clipping.");
                Assert.That(longRow.DesiredSize.X, Is.LessThanOrEqualTo(window.MinSize.X),
                    "The fixed columns alone are already wider than the window's own minimum width.");
            }

            window.Dispose();
            shortRow.Dispose();
            longRow.Dispose();
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>One Measure/Arrange pass at the window's own size, which is what a real open would do.</summary>
    private static void Layout(ClientSurveyWindow window)
    {
        Assert.DoesNotThrow(() =>
        {
            window.Measure(window.SetSize);
            window.Arrange(new UIBox2(Vector2.Zero, window.SetSize));
        }, "Laying the survey window out should not throw.");
    }

    /// <summary>A state with the shapes the window has to survive: no surface, no distance, no beacon, no rating.</summary>
    private static WFSurveyConsoleState SyntheticState(int count, string? firstName = null)
    {
        var state = new WFSurveyConsoleState { SystemName = System, ConsolePositionKnown = true };

        for (var i = 0; i < count; i++)
        {
            state.Planets.Add(new WFSurveyPlanetRow(
                new NetEntity(100 + i),
                i == 0 && firstName != null ? firstName : $"Body {i}",
                $"Beacon {i}",
                i % 2 == 0,
                new Vector2(i, i),
                i * 40f,
                i % 3 != 0,
                i % 2 == 0,
                i % 2 == 0,
                i % 4 == 0,
                WFVeinRating.Rich,
                i % 2 == 0));
        }

        return state;
    }

    /// <summary>
    /// Stands a star system up on a throwaway map the way a round does - StarSystemMapSystem.SetSystem is the same call
    /// the post-map-load hook makes - and puts a survey console on it at a known offset. With the feature on, this is
    /// also what registers Asclepiu and builds its z-stack.
    /// The bare surface is stamped by hand through the same production writer the registry uses, because the registry
    /// builds its planet-type map once in Initialize (WFPlanetRegistrySystem.cs:29-38) and the test harness loads
    /// [TestPrototypes] AFTER the server has started (Robust.UnitTesting/Pool/TestPair.cs:90-91), so a test-only
    /// surface can never be in that map. Nothing about the shipped path is being skipped: ApplySurface is exactly what
    /// RegisterPlanet calls.
    /// </summary>
    private static async Task<EntityUid> BuildSector(TestPair pair, bool feature)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();
        var console = EntityUid.Invalid;

        if (feature)
            await EnableFeature(pair);

        await server.WaitPost(() =>
        {
            var systems = server.System<StarSystemMapSystem>();
            var comp = entMan.EnsureComponent<StarSystemMapComponent>(map.MapUid);

            systems.SetSystem((map.MapUid, comp), System);

            console = entMan.SpawnEntity(ConsoleProto, new EntityCoordinates(map.MapUid, ConsolePos));

            if (!feature)
                return;

            var bare = FindBody(entMan, proto.Index<PlanetTypePrototype>(BareType).Name);

            Assert.That(bare, Is.Not.EqualTo(EntityUid.Invalid), $"The star system spawned no {BareType} body.");
            ApplySurfaceTo(pair, bare, BareSurface);
        });

        await server.WaitRunTicks(5);
        return console;
    }

    /// <summary>The spawned sector body carrying one display name.</summary>
    private static EntityUid FindBody(IEntityManager entMan, string name)
    {
        var query = entMan.AllEntityQueryEnumerator<FTLBeaconComponent, MetaDataComponent>();

        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityName == name)
                return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>
    /// Tears down every planet network the sector build left standing AND the sector map itself.
    /// The sector map has to go explicitly rather than ride the pool's own cleanup: TestPair.Cleanup deletes only the
    /// single most recent TestMap (Robust.UnitTesting/Pool/TestPair.Recycle.cs:48-55), so a test that calls
    /// CreateTestMap a second time would leave the star system - and the WFSectorPlanetComponent bodies on it - alive
    /// in a recycled pair, where BuildState's server-wide AllEntityQuery would join them into the next test's rows.
    /// Deleting a map the pool later deletes again is a no-op: DeleteEntity(EntityUid?) returns when the uid has no
    /// MetaDataComponent (RobustToolbox/Robust.Shared/GameObjects/EntityManager.cs:537-538).
    /// </summary>
    private static async Task TeardownSector(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var networks = server.System<WFPlanetNetworkSystem>();
            var found = new List<EntityUid>();
            var query = entMan.AllEntityQueryEnumerator<WFPlanetNetworkComponent>();

            while (query.MoveNext(out var uid, out _))
            {
                found.Add(uid);
            }

            foreach (var network in found)
            {
                networks.DeleteNetwork(network);
            }

            var maps = new List<EntityUid>();
            var mapQuery = entMan.AllEntityQueryEnumerator<StarSystemMapComponent>();

            while (mapQuery.MoveNext(out var uid, out _))
            {
                maps.Add(uid);
            }

            foreach (var map in maps)
            {
                entMan.DeleteEntity(map);
            }
        });

        await server.WaitRunTicks(5);
    }

    /// <summary>The row for one star-system body, by the display name its PlanetTypePrototype carries.</summary>
    private static WFSurveyPlanetRow Row(WFSurveyConsoleState state, string planetType, IPrototypeManager proto)
    {
        var name = proto.Index<PlanetTypePrototype>(planetType).Name;
        var row = state.Planets.FirstOrDefault(item => item.Name == name);

        Assert.That(row.Name, Is.EqualTo(name), $"The state has no row for {planetType}.");
        return row;
    }

    /// <summary>The registered sector body whose surface belongs to one planet type.</summary>
    private static EntityUid Body(IEntityManager entMan, IPrototypeManager proto, string planetType)
    {
        var query = entMan.AllEntityQueryEnumerator<WFSectorPlanetComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Surface is { } surface && proto.Index(surface).PlanetType == planetType)
                return uid;
        }

        Assert.Fail($"No sector body was registered for {planetType}.");
        return EntityUid.Invalid;
    }
}
