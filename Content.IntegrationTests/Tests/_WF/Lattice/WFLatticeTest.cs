using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Lattice;
using Content.Server._WF.Remade.Sector;
using Content.Shared._WF.Remade.Sector;
using Robust.Shared.Prototypes;
using Content.Shared._WF.DroneRings;
using Content.Shared._WF.Lattice;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>A sector map gets its primary anchors, bands fall by distance, and a lost anchor moves them.</summary>
public sealed class WFLatticeTest
{
    [Test]
    public async Task SetupSpawnsAnchorsAndBandsShiftWhenOneIsLost()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var transform = entities.System<SharedTransformSystem>();

        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);

            var anchors = new List<EntityUid>();
            var query = entities.EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid == map.MapUid)
                    anchors.Add(uid);
            }

            Assert.That(anchors, Has.Count.EqualTo(6));
            Assert.That(lattice.BandAt(map.MapUid, new Vector2(10000f, 0f)), Is.EqualTo(0));
            Assert.That(lattice.BandAt(map.MapUid, new Vector2(0f, 20000f)), Is.EqualTo(1));
            Assert.That(lattice.BandAt(map.MapUid, new Vector2(-23500f, 0f)), Is.EqualTo(2));
            Assert.That(lattice.BandAt(map.MapUid, new Vector2(0f, -27000f)), Is.EqualTo(3));
            Assert.That(lattice.BandAt(map.MapUid, new Vector2(35000f, 0f)), Is.EqualTo(4));

            // Setting up again keeps the anchors it has.
            lattice.Setup(map.MapUid);
            var count = 0;
            var again = entities.EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
            while (again.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapUid == map.MapUid)
                    count++;
            }

            Assert.That(count, Is.EqualTo(6));

            var lost = anchors[0];
            var direction = Vector2.Normalize(transform.GetWorldPosition(lost));
            Assert.That(lattice.BandAt(map.MapUid, direction * 16000f), Is.EqualTo(0));
            entities.DeleteEntity(lost);
            Assert.That(lattice.BandAt(map.MapUid, direction * 16000f), Is.EqualTo(1));
            Assert.That(lattice.BandAt(map.MapUid, -direction * 16000f), Is.EqualTo(0));
        });

        // Clients get the anchors that are left once the field next ticks.
        await server.WaitRunTicks(90);
        await server.WaitAssertion(() =>
        {
            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            Assert.That(field.Anchors, Has.Count.EqualTo(5));
            Assert.That(field.Strengths, Has.Count.EqualTo(5));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The bands follow a map's drone rings when there is one per edge, and the Deep garbles.</summary>
    [Test]
    public async Task BandsFollowDroneRingsAndTheDeepGarbles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();

        await server.WaitAssertion(() =>
        {
            entities.EnsureComponent<WFDroneRingsComponent>(map.MapUid).Radii = new List<float> { 10000f, 12000f, 14000f, 16000f };
            lattice.Setup(map.MapUid);

            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            Assert.That(field.Edges, Is.EqualTo(new List<float> { 0f, 10000f, 12000f, 14000f, 16000f }));
            Assert.That(lattice.Locate(map.MapUid, new Vector2(11000f, 0f)).Band, Is.EqualTo(1));
            Assert.That(lattice.Locate(map.MapUid, new Vector2(30000f, 0f)).Band, Is.EqualTo(4));

            const string text = "12345 67890";
            Assert.That(lattice.GarbleAt(text, map.MapUid, new Vector2(5000f, 0f), 1), Is.EqualTo(text));
            Assert.That(lattice.GarbleAt(text, map.MapUid, new Vector2(60000f, 0f), 1), Is.Not.EqualTo(text));

            var centres = new List<Robust.Shared.Map.MapCoordinates>();
            lattice.GetCentres(centres);
            Assert.That(centres, Does.Contain(new Robust.Shared.Map.MapCoordinates(Vector2.Zero, map.MapId)));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A Monolith hulk is only ever placed in the Deep.</summary>
    [Test]
    public async Task MonolithHulksArePlacedInTheDeep()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var sites = entities.System<WFRemadeSiteSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);
            Assert.That(sites.TryStartSite(protos.Index<WFRemadeSitePrototype>("WFRemadeSiteHulk"), null, false, out var encounter), Is.True);

            var origin = entities.GetComponent<WFEncounterComponent>(encounter).Origin;
            Assert.That(origin.MapId, Is.EqualTo(map.MapId));
            Assert.That(lattice.Locate(map.MapUid, origin.Position).Band, Is.EqualTo(4));
        });

        await pair.CleanReturnAsync();
    }
}
