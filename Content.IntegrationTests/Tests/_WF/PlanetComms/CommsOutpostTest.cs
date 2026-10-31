#nullable enable
using System.Linq;
using Content.Shared._WF.PlanetPois;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetComms;

/// <summary>Every world's site table places one comms outpost, and the outpost holds a mass transceiver.</summary>
[TestFixture]
public sealed class CommsOutpostTest
{
    private const string OutpostPath = "/Maps/_WF/PlanetComms/comms_outpost.yml";

    private static readonly string[] Tables =
    {
        "WFPoisAsclepiu", "WFPoisMerak", "WFPoisFervidus", "WFPoisThrascias", "WFPoisAerumna", "WFPoisCarcinoma",
    };

    [Test]
    public async Task EveryWorldGetsOneOutpostWithATransceiver()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in Tables)
            {
                var outposts = prototypes.Index<WFPlanetPoiTablePrototype>(id).Entries.Where(entry => entry.MapPath == OutpostPath).ToList();
                Assert.That(outposts, Has.Count.EqualTo(1), $"{id} has no comms outpost entry.");
                Assert.That(outposts[0].MinCount, Is.EqualTo(1), $"{id} doesn't always place its comms outpost.");
            }

            var maps = server.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, new ResPath(OutpostPath), out var grid), Is.True);
            var transceivers = server.EntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID.StartsWith("WFMassTransceiver") == true
                               && server.EntMan.GetComponent<TransformComponent>(meta.Owner).GridUid == grid!.Value.Owner);
            Assert.That(transceivers, Is.EqualTo(1), "The outpost should hold one mass transceiver.");
            maps.DeleteMap(mapId);
        });

        await pair.CleanReturnAsync();
    }
}
