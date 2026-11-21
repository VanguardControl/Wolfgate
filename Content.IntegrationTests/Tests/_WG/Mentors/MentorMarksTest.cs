#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.MentorHelp;
using Content.Server._WG.Mentors;
using Content.Server.Database;
using Content.Shared._WG.Mentors;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Mentors;

/// <summary>The mark sits on an on-duty mentor's body only: not off duty, not on a ghost, not after revocation.</summary>
[TestFixture]
[TestOf(typeof(WGMentorMarkTagSystem))]
public sealed class MentorMarksTest
{
    [Test]
    public async Task OnDutyBodiesOnly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var mentors = server.System<WGMentorSystem>();
        var session = players.Sessions.Single();

        EntityUid body = default;
        EntityUid ghost = default;
        await server.WaitPost(() =>
        {
            entities.System<SharedMapSystem>().CreateMap(out var mapId);
            body = entities.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
            ghost = entities.SpawnEntity("MobObserver", new MapCoordinates(2, 0, mapId));
            players.SetAttachedEntity(session, body);
        });
        await pair.RunTicksSync(5);
        Assert.That(entities.HasComponent<WGMentorMarkComponent>(body), Is.False);

        await MentorHelpTicketTest.Run(pair, async () =>
        {
            await mentors.GrantAsync(session.UserId, MentorTier.Senior, null, MentorRows.SourceStaff);
            return true;
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.TryGetComponent<WGMentorMarkComponent>(body, out var mark), "An on-duty mentor's body is marked.");
            Assert.That(mark!.Tier, Is.EqualTo(MentorTier.Senior));
            Assert.That(mark.ShowCrown);
            Assert.That(entities.HasComponent<WGMentorMarkComponent>(ghost), Is.False);

            mentors.SetDuty(session, false);
        });
        await pair.RunTicksSync(5);
        Assert.That(entities.HasComponent<WGMentorMarkComponent>(body), Is.False, "Off duty: no mark.");

        // Back on duty, then into a ghost: the ghost isn't marked and the body loses it.
        await server.WaitPost(() =>
        {
            mentors.SetDuty(session, true);
            players.SetAttachedEntity(session, ghost);
        });
        await pair.RunTicksSync(5);
        Assert.Multiple(() =>
        {
            Assert.That(entities.HasComponent<WGMentorMarkComponent>(ghost), Is.False, "Ghosts are never marked.");
            Assert.That(entities.HasComponent<WGMentorMarkComponent>(body), Is.False, "A body the mentor left isn't marked.");
        });

        // Back in the body, then revoked.
        await server.WaitPost(() => players.SetAttachedEntity(session, body));
        await pair.RunTicksSync(5);
        Assert.That(entities.HasComponent<WGMentorMarkComponent>(body));

        await MentorHelpTicketTest.Run(pair, () => mentors.RevokeAsync(session.UserId, null, "Testing"));
        await pair.RunTicksSync(5);
        Assert.That(entities.HasComponent<WGMentorMarkComponent>(body), Is.False, "Revoked: no mark.");

        await MentorHelpTicketTest.Run(pair, () => server.ResolveDependency<IServerDbManager>().DeleteMentorAsync(session.UserId.UserId));
        await pair.CleanReturnAsync();
    }
}
