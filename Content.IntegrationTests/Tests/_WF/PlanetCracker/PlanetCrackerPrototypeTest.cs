#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client.Gravity;
using Content.IntegrationTests.Pair;
using Content.Server.Construction.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Computer;
using Content.Shared.Decals;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Repairable;
using Content.Shared.UserInterface;
using Content.Shared.Wires;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// Everything the planet cracker prototypes promise that only a loaded server can check: that each one indexes and
/// spawns, that every sprite layer names a state its RSI actually has - borrowed sheets included - and the handful
/// of numbers the C# mirrors.
/// </summary>
[TestFixture]
public sealed class PlanetCrackerPrototypeTest
{
    /// <summary>
    /// Every prototype this feature adds, in the order the plan lists them.
    /// WFDeepVein and WFEffectSurveyPulse are deliberately NOT here. SpawnAll lays a FloorSteel grid on a
    /// map-initialised test map, so WFDeepVein's MapInit handler deletes itself twice over - the tile is not in its
    /// AllowedTiles and the grid resolves no WFPlanetLayer -> WFPlanetNetwork -> surface -> veins chain - and
    /// EveryPrototypeSpawns' EntityExists assert would fail. WFEffectSurveyPulse despawns after 0.48 s, which is
    /// shorter than EverySpriteStateExists' 15 ticks at net.tickrate 30 (0.50 s), and draws no sprite at all. Both are
    /// covered instead by DeepVeinTest and SurveyorTest, which spawn them where they survive long enough to read.
    /// </summary>
    private static readonly string[] Prototypes =
    {
        "WFGravityAnchor",
        "WFAnchorCrate",
        "WFAnchorCrateReplacement",
        "WFCentrifuge",
        "WFGravityProjector",
        "WFTransportGravgen",
        "WFThrusterLanding",
        "WFLandingThrusterKit",
        "WFCrackConsole",
        "WFSectorSurveyConsole",
        "WFChunkBerthMarker",
        "WFCentrifugeCircuitboard",
        "WFGravityProjectorCircuitboard",
        "WFEffectChunkBurst",
        "WFSurveyor",
        // Both miners are safely spawnable on the FloorSteel test grid, unlike WFDeepVein: nothing on a crack miner
        // deletes itself at MapInit, and an unanchored miner off a chunk simply never leaves Idle.
        "WFCrackMiner",
        "WFCrackMinerEmpty",
    };

    /// <summary>The rim ring's two decals, which F5 stamps by hand rather than through a tile prototype.</summary>
    private static readonly string[] RimDecals =
    {
        "WFCrackRimStraight",
        "WFCrackRimCurve",
    };

    /// <summary>The rim ring's own RSI, as both decal prototypes name it.</summary>
    private const string RimRsi = "/Textures/_WF/PlanetCracker/Decals/crack_rim.rsi";

    /// <summary>The prototypes that carry a Repairable block design D9's welder loop has to work on.</summary>
    private static readonly string[] Repairables =
    {
        "WFGravityAnchor",
        "WFCentrifuge",
        "WFGravityProjector",
    };

    private const string Projector = "WFGravityProjector";
    private const string Centrifuge = "WFCentrifuge";
    private const string CrackConsole = "WFCrackConsole";

    /// <summary>The sprite layer key every computer screen visualiser drives.</summary>
    private const string ScreenLayer = "computerLayerScreen";

    /// <summary>
    /// The screen RSI both consoles now name, which is the shared computer sheet: the screen layers dropped their own
    /// `sprite:` and inherit BaseComputer's, so every face has to be a state stock computers.rsi already ships.
    /// </summary>
    private const string ScreenRsi = "/Textures/Structures/Machines/computers.rsi";

    /// <summary>The sector survey console, which F2 gives a UserInterface block and a second screen visualiser.</summary>
    private const string SurveyConsole = "WFSectorSurveyConsole";

    /// <summary>The handheld surveyor's RSI: the stock anomaly locator, borrowed for its icon and both in-hands.</summary>
    private const string SurveyorRsi = "/Textures/Objects/Specific/Research/anomalylocator.rsi";

    /// <summary>The one state the surveyor prototype names; the locator's `screen` ships unused (plan D-N).</summary>
    private const string SurveyorState = "icon";

    /// <summary>UserInterfaceComponent.Interfaces, which the engine keeps internal.</summary>
    private static readonly FieldInfo InterfacesField = typeof(UserInterfaceComponent)
        .GetField("Interfaces", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private const string Crate = "WFAnchorCrate";
    private const string CrateReplacement = "WFAnchorCrateReplacement";
    private const string HullTile = "FloorSteel";

    /// <summary>Design section 5 / D17: the cargo-purchasable replacement crate.</summary>
    private const float ReplacementPrice = 400000f;

    [Test]
    public async Task EveryPrototypeSpawns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var spawned = await SpawnAll(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var (id, uid) in spawned)
                {
                    Assert.That(entMan.EntityExists(uid), Is.True, $"{id} did not survive being spawned.");
                    Assert.That(entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID, Is.EqualTo(id),
                        $"{id} spawned as something else.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The regression guard on "the prototypes reference their RSIs verbatim". A state name that is not in the
    /// meta.json only shows up as a missing sprite at runtime, and half of these sheets are now other people's art
    /// that this feature does not own and cannot stop from being renamed.
    /// </summary>
    [Test]
    public async Task EverySpriteStateExists()
    {
        // Connected, because the sprite layers only exist on the client half.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var clientEntMan = client.EntMan;

        var spawned = await SpawnAll(pair);
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var (id, serverUid) in spawned)
                {
                    var uid = pair.ToClientUid(serverUid);

                    Assert.That(clientEntMan.EntityExists(uid), Is.True, $"{id} never reached the client.");

                    if (!clientEntMan.TryGetComponent(uid, out SpriteComponent? sprite))
                        continue;

                    var layer = 0;

                    foreach (var spriteLayer in sprite.AllLayers)
                    {
                        layer++;

                        if (spriteLayer.RsiState.Name is not { } state)
                            continue;

                        Assert.That(spriteLayer.ActualRsi, Is.Not.Null,
                            $"{id} layer {layer} names state '{state}' but resolves no RSI.");
                        Assert.That(spriteLayer.ActualRsi!.TryGetState(state, out _), Is.True,
                            $"{id} layer {layer} names state '{state}', which its RSI does not have.");
                    }
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The client's gravity visualiser does an unconditional layer lookup for Core whenever the charge changes, so a
    /// centrifuge missing that layer throws on every client the moment it starts spinning up.
    /// </summary>
    [Test]
    public async Task CentrifugeMapsBothGravgenLayers()
    {
        // Connected, because the sprite layers only exist on the client half.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var clientEntMan = client.EntMan;

        var spawned = await SpawnAll(pair);
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var uid = pair.ToClientUid(spawned[Centrifuge]);
            var sprite = clientEntMan.GetComponent<SpriteComponent>(uid);
            var sprites = clientEntMan.System<SpriteSystem>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sprites.LayerMapTryGet((uid, sprite), GravityGeneratorVisualLayers.Base, out _, false), Is.True,
                    "The centrifuge does not map the gravity generator's Base layer.");
                Assert.That(sprites.LayerMapTryGet((uid, sprite), GravityGeneratorVisualLayers.Core, out _, false), Is.True,
                    "The centrifuge does not map the gravity generator's Core layer, which the client looks up unconditionally.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A child `visuals` mapping REPLACES its parent's wholesale instead of merging into it, so the crack console has to
    /// re-declare BaseComputer's two visualiser keys beside its own screen key. Trim either and the powered screen and
    /// the maintenance panel silently stop reacting, with no error anywhere. Three keys is the whole assertion.
    /// </summary>
    [Test]
    public async Task CrackConsoleKeepsInheritedVisualizerKeys()
    {
        // Read from the CLIENT's prototypes: the server registers GenericVisualizer as ignored
        // (ServerComponentFactory.cs:15), so the composed server-side prototype never carries the component.
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var protoMan = client.ResolveDependency<IPrototypeManager>();

        await client.WaitAssertion(() =>
        {
            var console = protoMan.Index<EntityPrototype>(CrackConsole);

            Assert.That(console.TryGetComponent<GenericVisualizerComponent>(out var visualizer,
                    client.ResolveDependency<IComponentFactory>()), Is.True,
                "The crack console has no GenericVisualizer at all; it lost even the inherited one.");

            var keys = visualizer!.Visuals.Keys.Select(key => key.ToString()).ToList();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(keys, Does.Contain(nameof(ComputerVisuals.Powered)),
                    "The re-declared ComputerVisuals.Powered block is gone, so the screen no longer blanks when unpowered.");
                Assert.That(keys, Does.Contain(nameof(WiresVisuals.MaintenancePanelState)),
                    "The re-declared WiresVisuals.MaintenancePanelState block is gone, so the maintenance panel no longer shows.");
                Assert.That(keys, Does.Contain(nameof(WFCrackConsoleVisuals.Screen)),
                    "The console's own screen key is missing.");
                Assert.That(keys, Has.Count.EqualTo(3),
                    $"Expected exactly the three visualiser keys; got {string.Join(", ", keys)}.");
            }

            // The Powered block must still drive the screen layer specifically: an entry that no longer names
            // computerLayerScreen would pass a key count while leaving the screen lit on a dead console.
            Assert.That(visualizer.Visuals.First(entry => entry.Key.ToString() == nameof(ComputerVisuals.Powered)).Value.Keys,
                Does.Contain(ScreenLayer),
                "The inherited Powered block no longer drives the screen layer.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An ActivatableUI is only as good as the window behind it: the interface block names its BoundUserInterface as a
    /// bare string, so a renamed or moved class is a runtime miss on the client with nothing to catch it at load.
    /// </summary>
    [Test]
    public async Task EveryActivatableUiResolvesItsInterface()
    {
        // The client is the side that constructs a BoundUserInterface at all.
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var protoMan = client.ResolveDependency<IPrototypeManager>();
        var compFactory = client.ResolveDependency<IComponentFactory>();
        var reflection = client.ResolveDependency<IReflectionManager>();

        await client.WaitAssertion(() =>
        {
            var checkedAny = false;

            using (Assert.EnterMultipleScope())
            {
                foreach (var id in Prototypes)
                {
                    var proto = protoMan.Index<EntityPrototype>(id);

                    if (!proto.TryGetComponent<ActivatableUIComponent>(out var activatable, compFactory))
                        continue;

                    Assert.That(activatable.Key, Is.Not.Null, $"{id} has an ActivatableUI with no key.");
                    Assert.That(proto.TryGetComponent<UserInterfaceComponent>(out var ui, compFactory), Is.True,
                        $"{id} has an ActivatableUI but no UserInterface block to open.");

                    // The interface map is internal to the engine component, so the composed prototype is read through
                    // the same field the engine itself resolves the client type from.
                    var interfaces = (Dictionary<Enum, InterfaceData>) InterfacesField.GetValue(ui)!;

                    Assert.That(interfaces.ContainsKey(activatable.Key!), Is.True,
                        $"{id}'s ActivatableUI key {activatable.Key} has no matching UserInterface entry.");

                    foreach (var (key, data) in interfaces)
                    {
                        checkedAny = true;

                        Assert.That(reflection.TryLooseGetType(data.ClientType, out var type), Is.True,
                            $"{id}'s {key} interface names {data.ClientType}, which no client type resolves to.");
                        Assert.That(typeof(BoundUserInterface).IsAssignableFrom(type), Is.True,
                            $"{id}'s {key} interface names {data.ClientType}, which is not a BoundUserInterface.");
                        Assert.That(type!.GetConstructor(new[] { typeof(EntityUid), typeof(Enum) }), Is.Not.Null,
                            $"{data.ClientType} has no (EntityUid, Enum) constructor for the engine to call.");
                    }
                }

                Assert.That(checkedAny, Is.True, "No planet cracker prototype carries an ActivatableUI at all.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The screen visualiser names its states as bare strings and EverySpriteStateExists only walks Sprite layers, so
    /// nothing else would notice a state the console's RSI does not have.
    /// </summary>
    [Test]
    public async Task CrackConsoleScreenStatesExist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var protoMan = client.ResolveDependency<IPrototypeManager>();
        var cache = client.ResolveDependency<IResourceCache>();

        await client.WaitAssertion(() =>
        {
            var console = protoMan.Index<EntityPrototype>(CrackConsole);

            Assert.That(console.TryGetComponent<GenericVisualizerComponent>(out var visualizer,
                    client.ResolveDependency<IComponentFactory>()), Is.True,
                "The crack console has no GenericVisualizer.");

            var screen = visualizer!.Visuals
                .First(entry => entry.Key.ToString() == nameof(WFCrackConsoleVisuals.Screen)).Value;

            Assert.That(screen.ContainsKey(ScreenLayer), Is.True,
                "The screen visualiser does not drive the screen layer.");

            var rsi = cache.GetResource<RSIResource>(new ResPath(ScreenRsi)).RSI;

            using (Assert.EnterMultipleScope())
            {
                foreach (var face in Enum.GetNames<WFCrackConsoleScreen>())
                {
                    Assert.That(screen[ScreenLayer].ContainsKey(face), Is.True,
                        $"The screen visualiser has no entry for the {face} face.");

                    var state = screen[ScreenLayer][face].State;

                    Assert.That(state, Is.Not.Null, $"The {face} face names no RSI state.");
                    Assert.That(rsi.TryGetState(state!, out _), Is.True,
                        $"The {face} face names state '{state}', which computers.rsi does not have.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The survey console's half of the same trap, one document further down the same file: its own screen key beside
    /// the two it has to re-declare, and two faces that have to name states computers.rsi actually ships.
    /// Its new UserInterface block is covered by EveryActivatableUiResolvesItsInterface, which walks the same array.
    /// </summary>
    [Test]
    public async Task SurveyConsoleScreenStatesExist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var protoMan = client.ResolveDependency<IPrototypeManager>();
        var cache = client.ResolveDependency<IResourceCache>();

        await client.WaitAssertion(() =>
        {
            var console = protoMan.Index<EntityPrototype>(SurveyConsole);

            Assert.That(console.TryGetComponent<GenericVisualizerComponent>(out var visualizer,
                    client.ResolveDependency<IComponentFactory>()), Is.True,
                "The survey console has no GenericVisualizer at all; it lost even the inherited one.");

            var keys = visualizer!.Visuals.Keys.Select(key => key.ToString()).ToList();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(keys, Does.Contain(nameof(ComputerVisuals.Powered)),
                    "The re-declared ComputerVisuals.Powered block is gone, so the screen no longer blanks when unpowered.");
                Assert.That(keys, Does.Contain(nameof(WiresVisuals.MaintenancePanelState)),
                    "The re-declared WiresVisuals.MaintenancePanelState block is gone, so the maintenance panel no longer shows.");
                Assert.That(keys, Does.Contain(nameof(WFSurveyConsoleVisuals.Screen)),
                    "The console's own screen key is missing.");
                Assert.That(keys, Has.Count.EqualTo(3),
                    $"Expected exactly the three visualiser keys; got {string.Join(", ", keys)}.");
            }

            var screen = visualizer.Visuals
                .First(entry => entry.Key.ToString() == nameof(WFSurveyConsoleVisuals.Screen)).Value;

            Assert.That(screen.ContainsKey(ScreenLayer), Is.True,
                "The survey console's screen visualiser does not drive the screen layer.");

            var rsi = cache.GetResource<RSIResource>(new ResPath(ScreenRsi)).RSI;

            using (Assert.EnterMultipleScope())
            {
                foreach (var face in Enum.GetNames<WFSurveyConsoleScreen>())
                {
                    Assert.That(screen[ScreenLayer].ContainsKey(face), Is.True,
                        $"The survey screen visualiser has no entry for the {face} face.");

                    var state = screen[ScreenLayer][face].State;

                    Assert.That(state, Is.Not.Null, $"The {face} face names no RSI state.");
                    Assert.That(rsi.TryGetState(state!, out _), Is.True,
                        $"The {face} face names state '{state}', which computers.rsi does not have.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The surveyor names one state and leans on its BaseRSI for the in-hands, so a borrowed RSI that has no `icon`,
    /// or has no in-hands to fall back on, is a missing sprite in every hand and nothing in the item block would say so.
    /// </summary>
    [Test]
    public async Task SurveyorSpriteResolves()
    {
        // Connected, because the sprite layers only exist on the client half.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var clientEntMan = client.EntMan;
        var cache = client.ResolveDependency<IResourceCache>();

        var spawned = await SpawnAll(pair);
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var uid = pair.ToClientUid(spawned["WFSurveyor"]);

            Assert.That(clientEntMan.TryGetComponent(uid, out SpriteComponent? sprite), Is.True,
                "The surveyor never reached the client with a sprite.");

            var rsi = cache.GetResource<RSIResource>(new ResPath(SurveyorRsi)).RSI;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(rsi.TryGetState(SurveyorState, out _), Is.True,
                    $"anomalylocator.rsi has no '{SurveyorState}' state for the prototype to name.");
                Assert.That(sprite!.AllLayers.Any(layer => layer.RsiState.Name == SurveyorState), Is.True,
                    $"The surveyor's sprite names no layer in the '{SurveyorState}' state.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The rim ring is decals on ordinary pinned ground rather than a tile prototype, so a renamed or mistyped state
    /// here is a silently missing ring on every cut circle and nothing else would notice: a decal prototype names its
    /// state as a bare SpriteSpecifier and the overlay only ever draws frame zero.
    /// </summary>
    [Test]
    public async Task CrackRimDecalsResolve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var protoMan = client.ResolveDependency<IPrototypeManager>();
        var cache = client.ResolveDependency<IResourceCache>();

        await client.WaitAssertion(() =>
        {
            var rsi = cache.GetResource<RSIResource>(new ResPath(RimRsi)).RSI;

            using (Assert.EnterMultipleScope())
            {
                foreach (var id in RimDecals)
                {
                    Assert.That(protoMan.TryIndex<DecalPrototype>(id, out var decal), Is.True,
                        $"{id} is not a decal prototype at all.");
                    Assert.That(decal!.Sprite, Is.InstanceOf<SpriteSpecifier.Rsi>(),
                        $"{id} does not name an RSI state, so the ring has nothing to draw.");

                    var state = ((SpriteSpecifier.Rsi)decal.Sprite).RsiState;

                    Assert.That(rsi.TryGetState(state, out _), Is.True,
                        $"{id} names state '{state}', which crack_rim.rsi does not have.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// BaseStructure narrows the Repairable default to Applicating only, so every one of these has to spell the welder
    /// out again or design D9's repair loop silently does nothing.
    /// </summary>
    [Test]
    public async Task EveryRepairableAcceptsAWelder()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var spawned = await SpawnAll(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var id in Repairables)
                {
                    Assert.That(entMan.TryGetComponent(spawned[id], out RepairableComponent? repairable), Is.True,
                        $"{id} has no Repairable block at all.");
                    Assert.That(repairable!.Qualities, Does.Contain("Welding"),
                        $"{id} cannot be repaired with a welder; it kept BaseStructure's Applicating-only narrowing.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A spawned machine is stocked with tier-one parts at map init, which is the multiplier's 1.0 baseline.</summary>
    [Test]
    public async Task ProjectorStartsAtTierOne()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var spawned = await SpawnAll(pair);

        await server.WaitAssertion(() =>
        {
            var uid = spawned[Projector];
            var containers = entMan.System<SharedContainerSystem>();

            Assert.That(containers.TryGetContainer(uid, MachineFrameComponent.PartContainerName, out var parts), Is.True,
                "The projector has no machine parts container.");
            Assert.That(parts!.ContainedEntities, Is.Not.Empty, "The projector was not stocked with any parts.");

            Assert.That(entMan.GetComponent<WFGravityProjectorComponent>(uid).CrackTimeMultiplier,
                Is.EqualTo(1f).Within(0.001f), "A tier-one projector does not sit at the full crack time.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The crates that ship with a hull must not inflate the appraisal the shipyard test measures; only the cargo
    /// replacement carries design section 5's price.
    /// </summary>
    [Test]
    public async Task ShippedCratesAreFree()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var spawned = await SpawnAll(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Price(entMan, spawned[Crate]), Is.EqualTo(0f).Within(0.001f),
                    "The shipped anchor crate is not free.");
                Assert.That(Price(entMan, spawned[CrateReplacement]), Is.EqualTo(ReplacementPrice).Within(0.001f),
                    "The replacement anchor crate is not priced at the design's 400000.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns one of every new prototype, each on its own tile of a throwaway grid.</summary>
    private static async Task<Dictionary<string, EntityUid>> SpawnAll(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        var map = await pair.CreateTestMap();
        var spawned = new Dictionary<string, EntityUid>();

        await server.WaitPost(() =>
        {
            var grid = map.Grid;
            var floor = new Tile(tileDefs[HullTile].TileId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

            // Three tiles apart, so nothing lands inside a neighbour's footprint or snap cell.
            for (var x = 0; x < Prototypes.Length * 3; x++)
            for (var y = 0; y < 3; y++)
            {
                tiles.Add((new Vector2i(x, y), floor));
            }

            maps.SetTiles(grid.Owner, grid.Comp, tiles);

            for (var i = 0; i < Prototypes.Length; i++)
            {
                var coords = new EntityCoordinates(grid.Owner, new Vector2(i * 3 + 1.5f, 1.5f));
                spawned[Prototypes[i]] = entMan.SpawnEntity(Prototypes[i], coords);
            }
        });

        await server.WaitRunTicks(5);
        return spawned;
    }

    /// <summary>The entity's static price, or zero when it carries none.</summary>
    private static float Price(IEntityManager entMan, EntityUid uid)
    {
        return entMan.TryGetComponent(uid, out StaticPriceComponent? price) ? (float)price.Price : 0f;
    }
}
