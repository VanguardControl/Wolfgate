#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._WG.MentorHelp;
using Content.Server._WG.Mentors;
using Content.Server.Database;
using Content.Shared._WG.Mentors;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._WG.Mentors;

/// <summary>Applying in each mode, reviews, the reapply wait, resigning, inactivity and promotion.</summary>
[TestFixture]
[TestOf(typeof(WGMentorApplicationSystem))]
public sealed class MentorApplicationTest
{
    private static readonly Dictionary<string, string> Answers = new() { ["q1"] = "To help.", ["q2"] = "Ships." };

    [Test]
    public async Task ModesReviewAndReapply()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var server = pair.Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var applications = server.System<WGMentorApplicationSystem>();
        var mentors = server.System<WGMentorSystem>();

        await server.WaitPost(() => cfg.SetCVar(MentorCVars.MinHours, 0f));
        var spotless = await MentorHelpTicketTest.FreshDummy(pair, "spotless");
        var queued = await MentorHelpTicketTest.FreshDummy(pair, "queued");

        // Hybrid on a spotless record: a Trainee at once.
        var refusal = await MentorHelpTicketTest.Run(pair, () => applications.ApplyCoreAsync(spotless.UserId, null, Answers, true));
        Assert.That(refusal, Is.Null);
        await MentorHelpTicketTest.WaitFor(pair, () => mentors.GetTier(spotless.UserId) == MentorTier.Trainee,
            "The spotless applicant never became a Trainee.");

        var stored = await MentorHelpTicketTest.Run(pair, () => db.GetMentorApplicationsAsync(null, spotless.UserId.UserId));
        Assert.Multiple(() =>
        {
            Assert.That(stored.Single().State, Is.EqualTo(WGMentorApplicationSystem.StateAutoApproved));
            Assert.That(stored.Single().Answers, Does.Contain("Ships."));
        });

        // Review mode: queued, and a second application waits behind it.
        await server.WaitPost(() => cfg.SetCVar(MentorCVars.ApplicationMode, "review"));
        Assert.That(await MentorHelpTicketTest.Run(pair, () => applications.ApplyCoreAsync(queued.UserId, null, Answers, true)), Is.Null);
        Assert.That(await MentorHelpTicketTest.Run(pair, () => applications.ApplyCoreAsync(queued.UserId, null, Answers, true)),
            Is.Not.Null, "One pending application at a time.");

        var pending = (await MentorHelpTicketTest.Run(pair, () =>
            db.GetMentorApplicationsAsync(WGMentorApplicationSystem.StatePending, queued.UserId.UserId))).Single();

        var page = await MentorHelpTicketTest.Run(pair, () => applications.BuildPageAsync(queued.UserId, null));
        Assert.That(page.Status, Is.EqualTo(MentorPageStatus.Pending));

        // A denial needs a reason, then sets the wait.
        Assert.That(await MentorHelpTicketTest.Run(pair, () => applications.ReviewAsync(pending.Id, false, "", 0, null)), Is.Not.Null);
        Assert.That(await MentorHelpTicketTest.Run(pair, () => applications.ReviewAsync(pending.Id, false, "Too new.", 7, null)), Is.Null);

        page = await MentorHelpTicketTest.Run(pair, () => applications.BuildPageAsync(queued.UserId, null));
        Assert.Multiple(() =>
        {
            Assert.That(page.Status, Is.EqualTo(MentorPageStatus.Apply));
            Assert.That(page.Blocks, Does.Contain(MentorBlock.Reapply));
            Assert.That(page.ReapplyAfter, Is.GreaterThan(DateTime.UtcNow.AddDays(6)));
            Assert.That(mentors.IsMentor(queued.UserId), Is.False);
        });

        Assert.That(await MentorHelpTicketTest.Run(pair, () => applications.ApplyCoreAsync(queued.UserId, null, Answers, true)),
            Is.Not.Null, "Refused inside the wait.");

        // Resigning puts the reapply wait on too.
        Assert.That(await MentorHelpTicketTest.Run(pair, () => mentors.ResignAsync(spotless.UserId)));
        page = await MentorHelpTicketTest.Run(pair, () => applications.BuildPageAsync(spotless.UserId, null));
        Assert.Multiple(() =>
        {
            Assert.That(mentors.IsMentor(spotless.UserId), Is.False);
            Assert.That(page.Blocks, Does.Contain(MentorBlock.Reapply));
        });

        await server.WaitPost(() =>
        {
            cfg.SetCVar(MentorCVars.ApplicationMode, MentorCVars.ApplicationMode.DefaultValue);
            cfg.SetCVar(MentorCVars.MinHours, MentorCVars.MinHours.DefaultValue);
        });
        await MentorHelpTicketTest.RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HoursBlockApplying()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var applications = pair.Server.System<WGMentorApplicationSystem>();
        var player = await MentorHelpTicketTest.FreshDummy(pair, "newcomer");

        var page = await MentorHelpTicketTest.Run(pair, () => applications.BuildPageAsync(player.UserId, null));
        Assert.Multiple(() =>
        {
            Assert.That(page.Status, Is.EqualTo(MentorPageStatus.Apply));
            Assert.That(page.Blocks, Does.Contain(MentorBlock.Hours));
            Assert.That(page.NeededHours, Is.EqualTo(MentorCVars.MinHours.DefaultValue));
        });

        Assert.That(await MentorHelpTicketTest.Run(pair, () => applications.ApplyCoreAsync(player.UserId, null, Answers, true)),
            Is.Not.Null);

        await MentorHelpTicketTest.RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InactivityAndPromotion()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, InLobby = true });
        var server = pair.Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var mentors = server.System<WGMentorSystem>();
        var mentor = await MentorHelpTicketTest.FreshDummy(pair, "trainee");

        await MentorHelpTicketTest.Run(pair, async () =>
        {
            await mentors.GrantAsync(mentor.UserId, MentorTier.Trainee, null, MentorRows.SourceStaff);
            return true;
        });

        // Inactive: off the roster's active list until they come back.
        Assert.That(await MentorHelpTicketTest.Run(pair, () => mentors.MarkInactiveAsync(mentor.UserId)));
        Assert.That(mentors.IsMentor(mentor.UserId), Is.False);
        Assert.That(await MentorHelpTicketTest.Run(pair, () => mentors.ReactivateAsync(mentor.UserId)));
        Assert.That(mentors.IsMentor(mentor.UserId));

        // One resolved, helpful ticket is enough with these settings.
        await server.WaitPost(() =>
        {
            cfg.SetCVar(MentorCVars.PromoteTickets, 1);
            cfg.SetCVar(MentorCVars.PromoteDays, 0);
        });

        await MentorHelpTicketTest.Run(pair, async () =>
        {
            await mentors.CheckPromotionAsync(mentor.UserId);
            return true;
        });
        Assert.That(mentors.GetTier(mentor.UserId), Is.EqualTo(MentorTier.Trainee), "Nothing resolved yet.");

        var now = DateTime.UtcNow;
        await MentorHelpTicketTest.Run(pair, () => db.AddMentorTicketAsync(new WolfgateMentorTicket
        {
            PlayerUserId = Guid.NewGuid(),
            CharacterName = "Asker",
            OpenedAt = now.AddMinutes(-5),
            FirstAnswerAt = now.AddMinutes(-4),
            ClaimedBy = mentor.UserId.UserId,
            ClosedAt = now,
            CloseReason = "resolved",
            Rating = 1,
        }));

        await MentorHelpTicketTest.Run(pair, async () =>
        {
            await mentors.CheckPromotionAsync(mentor.UserId);
            return true;
        });
        await MentorHelpTicketTest.WaitFor(pair, () => mentors.GetTier(mentor.UserId) == MentorTier.Mentor,
            "The Trainee was never promoted.");

        await server.WaitPost(() =>
        {
            cfg.SetCVar(MentorCVars.PromoteTickets, MentorCVars.PromoteTickets.DefaultValue);
            cfg.SetCVar(MentorCVars.PromoteDays, MentorCVars.PromoteDays.DefaultValue);
        });
        await MentorHelpTicketTest.RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }
}
