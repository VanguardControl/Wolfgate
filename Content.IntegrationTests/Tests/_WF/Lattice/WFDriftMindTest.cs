using System.Linq;
using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Lattice;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>Drift in a person's head: fed by a source nearby, by a Monolith, and fading once nothing feeds it.</summary>
public sealed class WFDriftMindTest
{
    [Test]
    public async Task ASourceFeedsAHeadThatFadesWithoutIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();

        EntityUid mob = default;
        EntityUid matter = default;
        await server.WaitAssertion(() =>
        {
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(players.Sessions.Single(), mob, force: true);
            matter = entities.SpawnEntity("WFMaterialExoticMatter", map.GridCoords.Offset(new Vector2(0.5f, 0f)));
        });

        await pair.RunSeconds(6f);
        var fed = 0f;
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.TryGetComponent<WFDriftComponent>(mob, out var drift), Is.True, "exotic matter put no Drift in a head beside it.");
            fed = drift!.Mind;
            Assert.That(fed, Is.GreaterThan(0.2f));
            Assert.That(drift.Flavour, Is.EqualTo(WFDriftFlavour.Unmooring));
            entities.DeleteEntity(matter);
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WFDriftComponent>(mob).Mind, Is.LessThan(fed), "Drift in a head didn't fade once the source was gone.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AMonolithPutsItsDriftInAHead()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();

        EntityUid mob = default;
        await server.WaitAssertion(() =>
        {
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(players.Sessions.Single(), mob, force: true);

            var stone = entities.SpawnEntity("WFMonolithScion", map.GridCoords.Offset(new Vector2(3f, 0f)));
            entities.GetComponent<WFRemadeDirectorComponent>(stone).BuildUp = false;
            entities.RemoveComponent<WFRemadeDirectorComponent>(stone);
            entities.GetComponent<WFMonolithComponent>(stone).Resonance = 100f;
        });

        await pair.RunSeconds(8f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.TryGetComponent<WFDriftComponent>(mob, out var drift), Is.True, "a Crescendo beside a player put no Drift in their head.");
            Assert.That(drift!.Mind, Is.GreaterThan(3f));
            Assert.That(drift.MindLevel, Is.GreaterThan(0));
            Assert.That(drift.Flavour, Is.EqualTo(WFDriftFlavour.Monolith));
        });

        await pair.CleanReturnAsync();
    }
}
