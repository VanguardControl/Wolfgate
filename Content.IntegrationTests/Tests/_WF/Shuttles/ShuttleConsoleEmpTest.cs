using System.Numerics;
using Content.Server.Emp;
using Content.Shared.Emp;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>
/// An EMP disables ordinary computers but leaves shuttle consoles running.
/// </summary>
public sealed class ShuttleConsoleEmpTest
{
    [Test]
    public async Task EmpSkipsShuttleConsole()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var emp = entManager.System<EmpSystem>();

        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f));
            var console = entManager.SpawnEntity("ComputerShuttle", coords);
            var other = entManager.SpawnEntity("ComputerAlert", coords);

            emp.EmpPulse(coords, 2f, 1000f, TimeSpan.FromSeconds(30));

            Assert.That(entManager.HasComponent<EmpDisabledComponent>(other), Is.True,
                "An ordinary computer should be disabled by the EMP.");
            Assert.That(entManager.HasComponent<EmpDisabledComponent>(console), Is.False,
                "A shuttle console should ignore the EMP.");
        });

        await pair.CleanReturnAsync();
    }
}
