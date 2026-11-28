#nullable enable
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.MentorHelp;
using Content.Server._WG.MentorHelp;
using Content.Server._WG.MentorObserve;
using Content.Server._WG.Mentors;
using Content.Shared._WG.MentorObserve;
using Content.Shared._WG.Mentors;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.MentorObserve;

/// <summary>A mentor steps out only after the asker allows it, the body is marked, and they come back when it is hurt.</summary>
[TestFixture]
[TestOf(typeof(WGMentorVisitSystem))]
public sealed class MentorObserveTest
{
    /// <summary>A dummy player with a mind in a human body on a fresh map.</summary>
    internal static async Task<(ICommonSession Session, EntityUid Body)> Embodied(TestPair pair, string prefix, MapId map)
    {
        var session = await MentorHelpTicketTest.FreshDummy(pair, prefix);
        EntityUid body = default;
        await pair.Server.WaitPost(() =>
        {
            var minds = pair.Server.System<SharedMindSystem>();
            body = pair.Server.EntMan.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map));
            var mind = minds.CreateMind(session.UserId, prefix);
            minds.TransferTo(mind.Owner, body, mind: mind.Comp);
        });

        return (session, body);
    }

    /// <summary>A mentor of the given tier with a body, an asker with a body and an open ticket the mentor holds.</summary>
    internal static async Task<(ICommonSession Mentor, EntityUid MentorBody, ICommonSession Asker, EntityUid AskerBody, int Ticket)> Setup(
        TestPair pair, MentorTier tier)
    {
        var server = pair.Server;
        var map = MapId.Nullspace;
        await server.WaitPost(() => server.System<SharedMapSystem>().CreateMap(out map));

        var (mentor, mentorBody) = await Embodied(pair, "mentor", map);
        var (asker, askerBody) = await Embodied(pair, "asker", map);
        var mentors = server.System<WGMentorSystem>();
        var help = server.System<WGMentorHelpSystem>();

        await MentorHelpTicketTest.Run(pair, async () =>
        {
            await mentors.GrantAsync(mentor.UserId, tier, null, MentorRows.SourceStaff);
            return true;
        });

        var ticket = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(mentors.IsOnDuty(mentor.UserId));
            Assert.That(help.Open(asker, "How do I fly?", null, false), Is.EqualTo(MentorHelpOpenResult.Opened));
            ticket = help.GetOpenTicket(asker.UserId)!.Id;
            Assert.That(help.PostAsResponder(mentor, ticket, "Let me look."));
        });

        return (mentor, mentorBody, asker, askerBody, ticket);
    }

    [Test]
    public async Task StepOutNeedsAllowAndEndsWhenHurt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var visits = server.System<WGMentorVisitSystem>();
        var (mentor, body, asker, askerBody, ticket) = await Setup(pair, MentorTier.Mentor);

        await server.WaitAssertion(() =>
        {
            var request = new WGMentorVisitRequestEvent { TicketId = ticket, Kind = MentorVisitKind.Watch };
            Assert.That(visits.RequestVisit(mentor, request), Is.Null);
            Assert.That(visits.VisitOf(mentor.UserId), Is.Null, "Nothing happens before the asker allows it.");
            Assert.That(visits.RequestVisit(mentor, request), Is.Not.Null, "One request at a time.");

            visits.AnswerVisit(asker, ticket, true);
            Assert.Multiple(() =>
            {
                Assert.That(visits.VisitOf(mentor.UserId)?.Kind, Is.EqualTo(MentorVisitKind.Watch));
                Assert.That(server.EntMan.HasComponent<WGMentorObserverComponent>(mentor.AttachedEntity));
                Assert.That(server.EntMan.HasComponent<WGMentorAwayComponent>(body), "The body is marked while away.");
                Assert.That(visits.IsVisited(asker.UserId));
            });
        });

        // Hurt while away: back in the body at once, and no stepping out again for now.
        await server.WaitAssertion(() =>
        {
            var damage = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 5);
            server.System<DamageableSystem>().TryChangeDamage(body, damage, true);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(visits.VisitOf(mentor.UserId), Is.Null);
                Assert.That(mentor.AttachedEntity, Is.EqualTo(body));
                Assert.That(server.EntMan.HasComponent<WGMentorAwayComponent>(body), Is.False);
            });

            var again = new WGMentorVisitRequestEvent { TicketId = ticket, Kind = MentorVisitKind.Watch };
            Assert.That(visits.RequestVisit(mentor, again), Is.Not.Null, "Refused right after being hurt.");
            server.System<WGMentorHelpSystem>().End(asker);
        });

        await MentorHelpTicketTest.RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TraineesDontStepOutAndRefusalWaitsForNothing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var visits = server.System<WGMentorVisitSystem>();
        var (mentor, _, asker, _, ticket) = await Setup(pair, MentorTier.Trainee);

        await server.WaitAssertion(() =>
        {
            var watch = new WGMentorVisitRequestEvent { TicketId = ticket, Kind = MentorVisitKind.Watch };
            Assert.That(visits.RequestVisit(mentor, watch), Is.Not.Null, "D4: a living Trainee can't step out.");

            var critter = new WGMentorVisitRequestEvent { TicketId = ticket, Kind = MentorVisitKind.Critter, Critter = "WGMentorCritterMouse" };
            Assert.That(visits.RequestVisit(mentor, critter), Is.Not.Null, "Nor join as a critter.");
            server.System<WGMentorHelpSystem>().End(asker);
        });

        await MentorHelpTicketTest.RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }
}
