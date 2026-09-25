#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Planets;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>Shared cavern test scaffolding, pulled in with <c>using static</c>.</summary>
public static class CavernFixture
{
    /// <summary>Every surface with a cavern below it.</summary>
    public static readonly string[] Surfaces =
    {
        "WFSurfaceAsclepiu",
        "WFSurfaceFervidus",
        "WFSurfaceMerak",
        "WFSurfaceAerumna",
        "WFSurfaceThrascias",
        "WFSurfaceCarcinoma",
    };

    /// <summary>A built world: its network, its surface layers ground first, and the cavern below it.</summary>
    public sealed class World
    {
        /// <summary>The planet network entity.</summary>
        public EntityUid Network;

        /// <summary>The network's surface layers, ground first and orbit last.</summary>
        public List<EntityUid> Layers = new();

        /// <summary>The network's maps below ground, nearest first.</summary>
        public List<EntityUid> LowerLayers = new();

        /// <summary>The cavern map, or Invalid when the world was built with caverns off.</summary>
        public EntityUid Cavern;

        /// <summary>The ground map.</summary>
        public EntityUid Ground => Layers[0];
    }

    /// <summary>Turns planet networks and caverns on for this pair; TestPair reverts both when the pair is returned.</summary>
    public static async Task EnableCaverns(TestPair pair)
    {
        await pair.Server.WaitPost(() =>
        {
            pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true);
            pair.Server.CfgMan.SetCVar(CavernCVars.Caverns, true);
        });
    }

    /// <summary>Builds an unowned stack of one surface at the origin.</summary>
    public static async Task<World> BuildWorld(TestPair pair, string surfaceId)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var world = new World();

        await server.WaitPost(() =>
        {
            var surface = proto.Index<WFPlanetSurfacePrototype>(surfaceId);
            var built = networks.BuildNetwork(surface, Vector2.Zero, surfaceId.Replace("WFSurface", string.Empty), null);

            Assert.That(built, Is.Not.Null, $"The {surfaceId} network failed to build.");

            var comp = entMan.GetComponent<WFPlanetNetworkComponent>(built!.Value);
            world.Network = built.Value;
            world.Layers = new List<EntityUid>(comp.Layers);
            world.LowerLayers = new List<EntityUid>(comp.LowerLayers);
            world.Cavern = comp.LowerLayers.FirstOrDefault();
        });

        await server.WaitRunTicks(1);
        return world;
    }

    /// <summary>Deletes the world's network, its cavern included.</summary>
    public static Task Teardown(TestPair pair, World world)
    {
        return PlanetFixture.Teardown(pair, world.Layers);
    }
}
