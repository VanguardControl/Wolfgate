using System.Collections.Generic;
using Content.Server.Administration.Managers;
using Content.Server._WF.AppearanceStudio;
using Content.Server._WF.FeatureBans;
using Content.Shared._WF.AppearanceStudio;
using Content.Shared._WF.CustomMarkings;
using Content.Shared._WF.FeatureBans;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.AppearanceStudio;

/// <summary>An Appearance Studio ban is made like a job ban and takes the studio's work off the player's body at once.</summary>
[TestFixture]
[TestOf(typeof(FeatureBanSystem))]
public sealed class StudioBanTest
{
    [Test]
    public async Task FeatureBanStripsBodyTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var session = pair.Player!;
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            mob = entMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(mob);
            humanoid.StudioTweaks = new List<StudioTweak> { new(StudioTarget.Worn, "head") { Scale = 1.5f } };
            humanoid.CustomMarkings = new List<CustomMarking> { new(new string('a', CustomMarkingRules.HashLength), CustomMarkingPlacement.Skin) };
            server.ResolveDependency<IPlayerManager>().SetAttachedEntity(session, mob);

            Assert.That(server.System<FeatureBanSystem>().IsBanned(session.UserId, StudioRules.Feature), Is.False);
            server.ResolveDependency<IBanManager>().CreateRoleBan(session.UserId, session.Name, null, null, null,
                StudioRules.Feature.Id, null, NoteSeverity.None, "test", DateTimeOffset.Now);
        });

        // The ban is stored asynchronously.
        var banned = false;
        for (var i = 0; i < 100 && !banned; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() => banned = server.System<FeatureBanSystem>().IsBanned(session.UserId, StudioRules.Feature));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(banned, Is.True, "the feature ban lands under its own prefix");
            Assert.That(server.ResolveDependency<IBanManager>().GetRoleBans(session.UserId),
                Does.Contain(WFFeatureBans.Role(StudioRules.Feature)));
            Assert.That(server.ResolveDependency<IBanManager>().GetJobBans(session.UserId), Is.Null.Or.Empty, "it is no job ban");

            var humanoid = server.EntMan.GetComponent<HumanoidAppearanceComponent>(mob);
            Assert.That(humanoid.StudioTweaks, Is.Empty, "the studio's tweaks come off");
            Assert.That(humanoid.CustomMarkings, Is.Empty, "custom markings come off too");

            server.EntMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }
}
