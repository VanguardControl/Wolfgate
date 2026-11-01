#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.Database;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.LiveFixes;

/// <summary>Saving a character must not break the unique job and trait indexes.</summary>
[TestFixture]
public sealed class CharacterSaveTest
{
    private static ServerDbSqlite CreateDb(Pair.TestPair pair)
    {
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        return new ServerDbSqlite(() => builder.Options,
            true,
            pair.Server.ResolveDependency<IConfigurationManager>(),
            true,
            pair.Server.ResolveDependency<ILogManager>().GetSawmill("db.ops"));
    }

    [Test]
    public async Task MovingHighPriorityToAnotherJobSaves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = CreateDb(pair);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, new HumanoidCharacterProfile());

        var jobs = pair.Server.ProtoMan.EnumeratePrototypes<JobPrototype>().Take(2).Select(j => new ProtoId<JobPrototype>(j.ID)).ToArray();
        var first = new HumanoidCharacterProfile().WithJobPriorities(new Dictionary<ProtoId<JobPrototype>, JobPriority>
        {
            { jobs[0], JobPriority.High },
            { jobs[1], JobPriority.Low },
        });
        var second = first.WithJobPriorities(new Dictionary<ProtoId<JobPrototype>, JobPriority>
        {
            { jobs[0], JobPriority.Low },
            { jobs[1], JobPriority.High },
        });

        await db.SaveCharacterSlotAsync(user, first, 0);
        await db.SaveCharacterSlotAsync(user, second, 0);

        var loaded = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
        Assert.That(loaded.JobPriorities[jobs[1]], Is.EqualTo(JobPriority.High));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TwoHighPriorityJobsSaveAsOne()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = CreateDb(pair);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, new HumanoidCharacterProfile());

        var jobs = pair.Server.ProtoMan.EnumeratePrototypes<JobPrototype>().Take(2).Select(j => new ProtoId<JobPrototype>(j.ID)).ToArray();
        var profile = new HumanoidCharacterProfile().WithJobPriorities(new Dictionary<ProtoId<JobPrototype>, JobPriority>
        {
            { jobs[0], JobPriority.High },
            { jobs[1], JobPriority.High },
        });

        await db.SaveCharacterSlotAsync(user, profile, 0);

        var loaded = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
        Assert.That(loaded.JobPriorities.Values.Count(p => p == JobPriority.High), Is.EqualTo(1));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ResavingKeepsTraits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = CreateDb(pair);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, new HumanoidCharacterProfile());

        var protoMan = pair.Server.ProtoMan;
        var trait = protoMan.EnumeratePrototypes<TraitPrototype>().First();
        var profile = new HumanoidCharacterProfile().WithTraitPreference(trait.ID, protoMan);

        await db.SaveCharacterSlotAsync(user, profile, 0);
        await db.SaveCharacterSlotAsync(user, profile, 0);

        var loaded = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
        Assert.That(loaded.TraitPreferences, Does.Contain(new ProtoId<TraitPrototype>(trait.ID)));
        await pair.CleanReturnAsync();
    }
}
