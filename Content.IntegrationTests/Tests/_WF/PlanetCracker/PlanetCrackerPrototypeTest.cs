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

/// <summary>Planet cracker prototypes: each spawns, every sprite state exists, mirrored numbers match.</summary>
[TestFixture]
public sealed class PlanetCrackerPrototypeTest
{
    /// <summary>Every spawnable prototype added; the deep vein and survey pulse are tested elsewhere.</summary>
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
        // Unlike the deep vein, miners survive the test grid; off a chunk they just stay Idle.
        "WFCrackMiner",
        "WFCrackMinerEmpty",
    };

    /// <summary>The rim ring's two decals.</summary>
    private static readonly string[] RimDecals =
    {
        "WFCrackRimStraight",
        "WFCrackRimCurve",
    };

    /// <summary>The rim ring's own RSI, as both decal prototypes name it.</summary>
    private const string RimRsi = "/Textures/_WF/PlanetCracker/Decals/crack_rim.rsi";

    /// <summary>The prototypes whose Repairable block must accept a welder.</summary>
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

    /// <summary>The shared computer sheet both consoles' screen layers inherit from BaseComputer.</summary>
    private const string ScreenRsi = "/Textures/Structures/Machines/computers.rsi";

    /// <summary>The sector survey console.</summary>
    private const string SurveyConsole = "WFSectorSurveyConsole";

    /// <summary>The handheld surveyor's borrowed RSI, the stock anomaly locator.</summary>
    private const string SurveyorRsi = "/Textures/Objects/Specific/Research/anomalylocator.rsi";

    /// <summary>The one state the surveyor prototype names.</summary>
    private const string SurveyorState = "icon";

    /// <summary>UserInterfaceComponent.Interfaces, which the engine keeps internal.</summary>
    private static readonly FieldInfo InterfacesField = typeof(UserInterfaceComponent)
        .GetField("Interfaces", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private const string Crate = "WFAnchorCrate";
    private const string CrateReplacement = "WFAnchorCrateReplacement";
    private const string HullTile = "FloorSteel";

    /// <summary>The price of the cargo-purchasable replacement crate.</summary>
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

    /// <summary>Every sprite layer names a state its RSI actually has, borrowed sheets included.</summary>
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

    /// <summary>The centrifuge maps both gravgen layers, which the client visualiser always looks up.</summary>
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

    /// <summary>The crack console re-declares BaseComputer's visualiser keys; child `visuals` replace them.</summary>
    [Test]
    public async Task CrackConsoleKeepsInheritedVisualizerKeys()
    {
        // Read on the client; the server ignores GenericVisualizer.
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

            // Powered must still drive the screen layer specifically.
            Assert.That(visualizer.Visuals.First(entry => entry.Key.ToString() == nameof(ComputerVisuals.Powered)).Value.Keys,
                Does.Contain(ScreenLayer),
                "The inherited Powered block no longer drives the screen layer.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every ActivatableUI's BoundUserInterface type name resolves on the client.</summary>
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

                    // The interface map is engine-internal, so read it by reflection.
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

    /// <summary>Every state the crack console's screen visualiser names exists in its RSI.</summary>
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

    /// <summary>The survey console keeps the inherited visualiser keys and its screen states exist.</summary>
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

    /// <summary>The surveyor's borrowed RSI has its icon state and in-hand states.</summary>
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

    /// <summary>Both rim decals resolve and name real states of the rim RSI.</summary>
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

    /// <summary>Every repairable prototype re-declares the welder, which BaseStructure's default drops.</summary>
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

    /// <summary>A spawned projector starts with tier-one parts, the multiplier's 1.0 baseline.</summary>
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

    /// <summary>Crates that ship with a hull are free; only the cargo replacement carries a price.</summary>
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

            // Three tiles apart, clear of each neighbour's footprint.
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
