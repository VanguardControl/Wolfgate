using System.Linq;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Reports from the 2026-11-10 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1110Test
{
    /// <summary>Report 708: a body with no legs is down on the floor but can still use what it reaches.</summary>
    [Test]
    public async Task LeglessBodyCanStillInteract()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid body = default, locker = default;
        await server.WaitPost(() =>
        {
            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            locker = entMan.SpawnEntity("LockerFreezer", map.GridCoords);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitPost(() =>
        {
            var bodySystem = entMan.System<SharedBodySystem>();
            var limbs = bodySystem.GetBodyChildrenOfType(body, BodyPartType.Leg)
                .Concat(bodySystem.GetBodyChildrenOfType(body, BodyPartType.Foot))
                .Select(part => part.Id)
                .ToList();
            Assert.That(limbs, Is.Not.Empty, "Precondition: the body has no legs to take.");
            foreach (var limb in limbs)
                entMan.DeleteEntity(limb);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var consciousness = entMan.GetComponent<WolfmedConsciousnessComponent>(body);
            Assert.That(consciousness.State, Is.EqualTo(WolfmedConsciousness.Downed), "Precondition: a legless body is not down.");
            Assert.That(consciousness.Cause, Is.EqualTo(WolfmedCause.Legs), "Precondition: something besides the legs holds the body down.");
            Assert.That(entMan.System<ActionBlockerSystem>().CanInteract(body, locker), Is.True,
                "a legless body cannot use the locker beside it.");
        });

        await pair.CleanReturnAsync();
    }
}
