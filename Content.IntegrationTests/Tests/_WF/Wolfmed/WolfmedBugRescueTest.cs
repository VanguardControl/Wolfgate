#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Commands;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Players;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// TEMPORARY (playtest 5): healmeimbroken heals the player's body, files a ticket naming the reason and the wounds it
/// found, and refuses a second use inside the cooldown. Delete with the command.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedBugRescueSystem))]
public sealed class WolfmedBugRescueTest : GameTest
{
    [Test]
    public async Task RescueHealsReportsAndCoolsDownTest()
    {
        var map = await Pair.CreateTestMap();
        var minds = SEntMan.System<SharedMindSystem>();
        var session = Server.PlayerMan.GetSessionById(Client.Session!.UserId);
        var rescue = SEntMan.System<WolfmedBugRescueSystem>();
        EntityUid body = default;

        await Server.WaitPost(() =>
        {
            minds.WipeMind(session.ContentData()?.Mind);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, body);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(body), "the player is not in the body.");
            var slash = SProtoMan.Index<DamageTypePrototype>("Slash");
            SEntMan.System<DamageableSystem>().TryChangeDamage(body, new DamageSpecifier(slash, FixedPoint2.New(40)),
                origin: null, targetPart: TargetBodyPart.Torso);
            var torso = SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, BodyPartType.Torso).Single().Id;
            var wounds = SEntMan.System<WoundSystem>();
            Assert.That(wounds.GetWounds(torso).Any(), Is.True, "the slash left no wound.");

            var report = rescue.BuildReport(session, body, "the autodoc ate my arm");
            TestContext.Out.WriteLine(report);
            Assert.That(report, Does.Contain("healmeimbroken").And.Contain("the autodoc ate my arm").And.Contain("torso"),
                "the ticket does not carry the command, the reason and the wounded part.");

            Assert.That(rescue.Rescue(session, "the autodoc ate my arm"), Is.True, "the rescue was refused.");
            Assert.That(wounds.GetWounds(torso).Any(wound => wound.Comp.State == WoundState.Open), Is.False,
                "the rescue did not heal the wound.");
            Assert.That(rescue.Rescue(session, "again"), Is.False, "the cooldown did not hold.");
        });
    }
}
