#nullable enable
using System.Numerics;
using System.Reflection;
using Content.Server.Movement.Components;
using Content.Server.Movement.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Moving a pulled thing with the pointer, when the pointer isn't anywhere the puller can reach.</summary>
[TestFixture]
public sealed class PullPointerTest
{
    /// <summary>A pointer on another map or at a NaN position is ignored; one on the puller's map still works.</summary>
    [Test]
    public async Task APointerOffThePullersMapIsIgnored()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var otherMap = await pair.CreateTestMap();
        var session = await server.AddDummySession();

        var puller = EntityUid.Invalid;
        var pulled = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            puller = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.MapUid, Vector2.Zero));
            pulled = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.MapUid, new Vector2(1f, 0f)));
            server.PlayerMan.SetAttachedEntity(session, puller);
            Assert.That(entMan.System<PullingSystem>().TryStartPull(puller, pulled), Is.True, "The puller could not start pulling.");
        });
        await server.WaitRunTicks(2);

        var handler = typeof(PullController).GetMethod("OnRequestMovePulledObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var controller = entMan.System<PullController>();
        void Point(EntityCoordinates coords) => handler.Invoke(controller, new object?[] { session, coords, puller });

        await server.WaitAssertion(() =>
        {
            Point(new EntityCoordinates(otherMap.MapUid, new Vector2(3f, 3f)));
            Assert.That(entMan.HasComponent<PullMovingComponent>(pulled), Is.False, "A pointer on another map moved the pulled mob.");

            Point(new EntityCoordinates(puller, new Vector2(float.NaN, 0f)));
            Assert.That(entMan.HasComponent<PullMovingComponent>(pulled), Is.False, "A NaN pointer moved the pulled mob.");

            Point(new EntityCoordinates(puller, new Vector2(5f, 0f)));
            Assert.That(entMan.HasComponent<PullMovingComponent>(pulled), Is.True, "A pointer on the puller's map no longer moves the pulled mob.");
        });

        await pair.CleanReturnAsync();
    }
}
