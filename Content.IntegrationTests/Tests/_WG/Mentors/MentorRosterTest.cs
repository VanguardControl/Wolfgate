#nullable enable
using System.Linq;
using Content.Client._WG.Mentors;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Mentors;
using Content.Server.Database;
using Content.Shared._WG.Mentors;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WG.Mentors;

/// <summary>Staff make a mentor, the client hears of it, duty follows the player's choice and survives reconnects.</summary>
[TestFixture]
[TestOf(typeof(WGMentorSystem))]
public sealed class MentorRosterTest
{
    [Test]
    public async Task GrantDutyTierRevoke()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;

        var players = server.ResolveDependency<IPlayerManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var console = server.ResolveDependency<IConsoleHost>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var clientCfg = client.ResolveDependency<IConfigurationManager>();
        var mentors = server.System<WGMentorSystem>();
        WGClientMentorSystem Seen() => client.System<WGClientMentorSystem>();

        var session = players.Sessions.Single();
        var user = session.UserId;
        var name = session.Name;

        Assert.That(mentors.IsMentor(user), Is.False);

        // Granted: on the roster as a Trainee, on duty, and the client knows.
        await Run(pair, async () =>
        {
            await mentors.GrantAsync(user, MentorTier.Trainee, null, MentorRows.SourceStaff);
            return true;
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(mentors.IsMentor(user));
                Assert.That(mentors.IsOnDuty(user));
                Assert.That(mentors.GetTier(user), Is.EqualTo(MentorTier.Trainee));
                Assert.That(mentors.OnDutyCount, Is.EqualTo(1));
            });
        });
        Assert.Multiple(() =>
        {
            Assert.That(Seen().IsMentor);
            Assert.That(Seen().OnDuty);
            Assert.That(Seen().Tier, Is.EqualTo(MentorTier.Trainee));
            Assert.That(Seen().OnDutyCount, Is.EqualTo(1));
        });

        // Off duty from the client, then on again from the console.
        await client.WaitPost(() => Seen().RequestDuty(false));
        await pair.RunTicksSync(5);
        Assert.Multiple(() =>
        {
            Assert.That(Seen().OnDuty, Is.False);
            Assert.That(Seen().OnDutyCount, Is.EqualTo(0));
        });
        await server.WaitAssertion(() => Assert.That(mentors.IsOnDuty(user), Is.False));

        await server.WaitPost(() => console.ExecuteCommand(session, "mentor on"));
        await pair.RunTicksSync(5);
        Assert.That(Seen().OnDuty);

        // A reconnect loads the row again and starts on duty; with the client's choice set, off duty.
        await Reconnect(pair, clientNet, name);
        session = players.Sessions.Single();
        await WaitFor(pair, () => mentors.IsMentor(user), "The mentor row never loaded after reconnecting.");
        await pair.RunTicksSync(5);
        Assert.Multiple(() =>
        {
            Assert.That(Seen().IsMentor);
            Assert.That(Seen().OnDuty);
        });

        await client.WaitPost(() => clientCfg.SetCVar(MentorCVars.StartOffDuty, true));
        await pair.RunTicksSync(5);
        await Reconnect(pair, clientNet, name);
        session = players.Sessions.Single();
        await WaitFor(pair, () => mentors.IsMentor(user), "The mentor row never loaded after the second reconnect.");
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(mentors.IsOnDuty(user), Is.False, "Should start off duty."));
        Assert.That(Seen().OnDuty, Is.False);
        await client.WaitPost(() => clientCfg.SetCVar(MentorCVars.StartOffDuty, false));

        // Promotion keeps duty and the grant date; the row records it.
        Assert.That(await Run(pair, () => mentors.SetTierAsync(user, MentorTier.Mentor, null)), Is.True);
        await pair.RunTicksSync(5);
        Assert.That(Seen().Tier, Is.EqualTo(MentorTier.Mentor));

        var row = await Run(pair, () => db.GetMentorAsync(user.UserId));
        Assert.Multiple(() =>
        {
            Assert.That(row!.Tier, Is.EqualTo("mentor"));
            Assert.That(row.PromotedAt, Is.Not.Null);
            Assert.That(row.Source, Is.EqualTo(MentorRows.SourceStaff));
        });

        // Revoked: off the roster, the client is told, the row keeps its history.
        Assert.That(await Run(pair, () => mentors.RevokeAsync(user, null, "Testing")), Is.True);
        Assert.That(await Run(pair, () => mentors.RevokeAsync(user, null, "Again")), Is.False);
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(mentors.IsMentor(user), Is.False);
                Assert.That(mentors.OnDutyCount, Is.EqualTo(0));
            });
        });
        Assert.That(Seen().IsMentor, Is.False);

        row = await Run(pair, () => db.GetMentorAsync(user.UserId));
        Assert.Multiple(() =>
        {
            Assert.That(row!.State, Is.EqualTo(MentorRows.StateRevoked));
            Assert.That(row.History, Does.Contain("granted trainee").And.Contain("tier mentor").And.Contain("Testing"));
        });

        // A revoked player is not loaded as a mentor on reconnect, and can't go on duty.
        await Reconnect(pair, clientNet, name);
        session = players.Sessions.Single();
        await pair.RunTicksSync(20);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(mentors.IsMentor(user), Is.False);
                Assert.That(mentors.SetDuty(session, true), Is.False);
            });
        });

        await Run(pair, () => db.DeleteMentorAsync(user.UserId));
        await pair.CleanReturnAsync();
    }

    private static async Task Reconnect(TestPair pair, IClientNetManager clientNet, string name)
    {
        await pair.Client.WaitPost(() => clientNet.ClientDisconnect("For testing"));
        await pair.RunTicksSync(10);
        await pair.Client.WaitPost(() => clientNet.ClientConnect(null!, 0, name));
        await pair.RunTicksSync(10);
    }

    private static async Task WaitFor(TestPair pair, Func<bool> done, string message)
    {
        var met = false;
        for (var i = 0; i < 300 && !met; i++)
        {
            await pair.RunTicksSync(1);
            await pair.Server.WaitPost(() => met = done());
        }

        Assert.That(met, message);
    }

    private static async Task<T> Run<T>(TestPair pair, Func<Task<T>> start)
    {
        Task<T> task = null!;
        await pair.Server.WaitPost(() => task = start());
        for (var i = 0; i < 600 && !task.IsCompleted; i++)
        {
            await pair.RunTicksSync(1);
            await Task.Delay(5);
        }

        Assert.That(task.IsCompleted, Is.True, "The server call never finished.");
        return await task;
    }
}
