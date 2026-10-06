using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Database;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.UnitTesting;
using SixLabors.ImageSharp.PixelFormats;
using ClientCustomMarkingSystem = Content.Client._WF.CustomMarkings.CustomMarkingSystem;
using ServerCustomMarkingSystem = Content.Server._WF.CustomMarkings.CustomMarkingSystem;

namespace Content.IntegrationTests.Tests._WF.CustomMarkings;

/// <summary>
/// The worn list through validation and the database, the library and art tables, and a marking's whole trip:
/// saved by a client, worn by a body, fetched and drawn, then blocked.
/// </summary>
[TestFixture]
[TestOf(typeof(ServerCustomMarkingSystem))]
public sealed class CustomMarkingLibraryTest
{
    private const int MaxWaitTicks = 300;

    private static readonly string HashA = new('a', CustomMarkingRules.HashLength);
    private static readonly string HashB = new('b', CustomMarkingRules.HashLength);

    [Test]
    public async Task ProfileRoundTripTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var db = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());

        var worn = new List<CustomMarking>
        {
            new(HashA, CustomMarkingPlacement.Behind),
            new(HashB, CustomMarkingPlacement.Front),
        };

        HumanoidCharacterProfile valid = default!;
        await server.WaitAssertion(() =>
        {
            var crafted = new List<CustomMarking>(worn) { new("junk", CustomMarkingPlacement.Skin), worn[0] };
            for (var i = 0; i < 20; i++)
            {
                crafted.Add(new CustomMarking(i.ToString("x64"), CustomMarkingPlacement.Skin));
            }

            var profile = HumanoidCharacterProfile.DefaultWithSpecies();
            valid = (HumanoidCharacterProfile) profile.WithCustomMarkings(worn).Validated(pair.Player!, IoCManager.Instance!);
            var cut = (HumanoidCharacterProfile) profile.WithCustomMarkings(crafted).Validated(pair.Player!, IoCManager.Instance!);
            var max = server.CfgMan.GetCVar(CustomMarkingCVars.MaxWorn);

            Assert.Multiple(() =>
            {
                Assert.That(valid.CustomMarkings, Is.EqualTo(worn), "validation keeps a good list");
                Assert.That(cut.CustomMarkings, Has.Count.EqualTo(max), "validation cuts a list to the server's limit");
                Assert.That(cut.CustomMarkings.Take(2), Is.EqualTo(worn), "and drops what isn't a marking");
                Assert.That(valid.Clone().CustomMarkings, Is.EqualTo(worn), "copies keep the list");
                Assert.That(valid.Clone().CustomMarkings, Is.Not.SameAs(valid.CustomMarkings), "as their own");
                Assert.That(valid.MemberwiseEquals(valid.Clone()), Is.True);
                Assert.That(valid.MemberwiseEquals(valid.WithCustomMarkings(worn.Take(1))), Is.False);
            });

            // A character exported to a file and imported again.
            var humanoids = server.System<SharedHumanoidAppearanceSystem>();
            using var file = new MemoryStream();
            using (var writer = new StreamWriter(file, leaveOpen: true))
            {
                humanoids.ToDataNode(valid).Write(writer);
            }

            file.Position = 0;
            Assert.That(humanoids.FromStream(file, pair.Player!).CustomMarkings, Is.EqualTo(worn), "an exported character keeps the list");
        });

        await db.InitPrefsAsync(user, valid);
        var prefs = await db.GetPlayerPreferencesAsync(user);
        var loaded = (HumanoidCharacterProfile) prefs!.Characters.Single().Value;
        Assert.That(loaded.CustomMarkings, Is.EqualTo(worn));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LibraryStorageTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = GetDb(pair.Server);
        var user = Guid.NewGuid();
        var other = Guid.NewGuid();
        var pngA = new byte[] { 1, 2, 3 };
        var pngB = new byte[] { 4, 5 };
        const int limit = 2;

        var added = await db.SaveCustomMarkingAsync(user, 0, "One", (int) CustomMarkingPlacement.Skin, HashA, pngA, limit);
        Assert.That(added.Error, Is.Null);
        var id = added.Entry!.Id;
        Assert.Multiple(() =>
        {
            Assert.That(id, Is.GreaterThan(0));
            Assert.That(added.Entry.ArtHash, Is.EqualTo(HashA));
            Assert.That(added.PreviousHash, Is.Null);
        });
        Assert.That(await db.GetCustomMarkingArtAsync(HashA), Is.EqualTo(pngA));

        // A change without art keeps the art.
        var renamed = await db.SaveCustomMarkingAsync(user, id, "Renamed", (int) CustomMarkingPlacement.Hair, null, null, limit);
        Assert.Multiple(() =>
        {
            Assert.That(renamed.Entry!.Name, Is.EqualTo("Renamed"));
            Assert.That(renamed.Entry.Placement, Is.EqualTo((int) CustomMarkingPlacement.Hair));
            Assert.That(renamed.Entry.ArtHash, Is.EqualTo(HashA));
            Assert.That(renamed.PreviousHash, Is.Null);
        });

        // New art replaces the entry's, and the old art stays for characters still wearing it.
        var redrawn = await db.SaveCustomMarkingAsync(user, id, "Renamed", (int) CustomMarkingPlacement.Hair, HashB, pngB, limit);
        Assert.Multiple(() =>
        {
            Assert.That(redrawn.Entry!.ArtHash, Is.EqualTo(HashB));
            Assert.That(redrawn.PreviousHash, Is.EqualTo(HashA));
        });
        Assert.That(await db.GetCustomMarkingArtAsync(HashA), Is.EqualTo(pngA));

        // Art another player already saved is shared, not stored twice or overwritten.
        var shared = await db.SaveCustomMarkingAsync(other, 0, "Theirs", (int) CustomMarkingPlacement.Skin, HashB, new byte[] { 9 }, limit);
        Assert.That(shared.Error, Is.Null);
        Assert.That(await db.GetCustomMarkingArtAsync(HashB), Is.EqualTo(pngB));

        Assert.That((await db.SaveCustomMarkingAsync(other, id, "Stolen", 0, null, null, limit)).Error,
            Is.EqualTo("wf-custom-marking-error-missing"), "another player's entry can't be changed");
        Assert.That(await db.DeleteCustomMarkingAsync(other, id), Is.False, "or deleted");
        Assert.That((await db.SaveCustomMarkingAsync(user, 0, "No art", 0, null, null, limit)).Error,
            Is.EqualTo("wf-custom-marking-error-invalid"), "a new entry needs art");

        Assert.That((await db.SaveCustomMarkingAsync(user, 0, "Two", 0, HashA, pngA, limit)).Error, Is.Null);
        Assert.That((await db.SaveCustomMarkingAsync(user, 0, "Three", 0, HashA, pngA, limit)).Error,
            Is.EqualTo("wf-custom-marking-error-full"));
        Assert.That((await db.SaveCustomMarkingAsync(user, id, "Still editable", 0, null, null, limit)).Error, Is.Null,
            "a full library can still be changed");

        var library = await db.GetCustomMarkingsAsync(user);
        Assert.That(library.Select(entry => entry.Name), Is.EqualTo(new[] { "Still editable", "Two" }));
        Assert.That(await db.GetCustomMarkingsAsync(other), Has.Count.EqualTo(1));

        // Blocking hides art from everyone and refuses it from then on.
        Assert.That(await db.SetCustomMarkingArtBlockedAsync(HashA, true), Is.EqualTo(user), "reports who first saved it");
        Assert.That(await db.SetCustomMarkingArtBlockedAsync(new string('c', CustomMarkingRules.HashLength), true), Is.Null);
        Assert.That(await db.GetCustomMarkingArtAsync(HashA), Is.Null);
        Assert.That((await db.SaveCustomMarkingAsync(other, 0, "Again", 0, HashA, pngA, limit)).Error,
            Is.EqualTo("wf-custom-marking-error-blocked"));
        Assert.That(await db.SetCustomMarkingArtBlockedAsync(HashA, false), Is.EqualTo(user));
        Assert.That(await db.GetCustomMarkingArtAsync(HashA), Is.EqualTo(pngA));

        Assert.That(await db.DeleteCustomMarkingAsync(user, id), Is.True);
        Assert.That(await db.GetCustomMarkingsAsync(user), Has.Count.EqualTo(1));
        Assert.That(await db.GetCustomMarkingArtAsync(HashB), Is.EqualTo(pngB), "deleting an entry leaves its art");

        await pair.CleanReturnAsync();
    }

    /// <summary>Profiles put their markings on bodies, copies of a body keep them, and the setting turns them off.</summary>
    [Test]
    public async Task BodyWearsProfileMarkingsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var worn = new List<CustomMarking> { new(HashA, CustomMarkingPlacement.Skin), new(HashB, CustomMarkingPlacement.Front) };

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var humanoids = entMan.System<SharedHumanoidAppearanceSystem>();
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithCustomMarkings(worn);

            var body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            humanoids.LoadProfile(body, profile);
            Assert.That(entMan.GetComponent<HumanoidAppearanceComponent>(body).CustomMarkings, Is.EqualTo(worn));

            var clone = entMan.SpawnEntity("MobHuman", map.GridCoords);
            humanoids.CloneAppearance(body, clone);
            var copied = entMan.GetComponent<HumanoidAppearanceComponent>(clone).CustomMarkings;
            Assert.That(copied, Is.EqualTo(worn));
            Assert.That(copied, Is.Not.SameAs(entMan.GetComponent<HumanoidAppearanceComponent>(body).CustomMarkings));

            humanoids.LoadProfile(body, profile.WithCustomMarkings(new List<CustomMarking>()));
            Assert.That(entMan.GetComponent<HumanoidAppearanceComponent>(body).CustomMarkings, Is.Empty, "a profile without any takes them off");

            server.CfgMan.SetCVar(CustomMarkingCVars.Enabled, false);
            try
            {
                humanoids.LoadProfile(body, profile);
                Assert.That(entMan.GetComponent<HumanoidAppearanceComponent>(body).CustomMarkings, Is.Empty, "the setting turns them off");
            }
            finally
            {
                server.CfgMan.SetCVar(CustomMarkingCVars.Enabled, true);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SavedMarkingIsDrawnTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var cSystem = client.System<ClientCustomMarkingSystem>();
        var sSystem = server.System<ServerCustomMarkingSystem>();

        var art = new CustomMarkingArt();
        art.SetPixel(CustomMarkingArt.South, 15, 10, new Rgba32(200, 30, 30, 255));
        art.SetPixel(CustomMarkingArt.West, 2, 3, new Rgba32(0, 0, 0, 90));
        var hash = ServerCustomMarkingSystem.Hash(art);

        CustomMarkingSaveResultEvent answer = null;
        void OnAnswer(CustomMarkingSaveResultEvent ev) => answer = ev;
        await client.WaitPost(() => cSystem.SaveAnswered += OnAnswer);

        // Something that isn't a drawing is refused.
        await client.WaitPost(() => cSystem.Save(0, "Blank", CustomMarkingPlacement.Skin, new CustomMarkingArt()));
        await WaitFor(pair, () => answer != null, "the server to answer the blank save");
        Assert.That(answer.Error, Is.EqualTo("wf-custom-marking-error-blank"));
        Assert.That(answer.Entry, Is.Null);

        // The client saves a drawing to its library.
        answer = null;
        var request = 0;
        await client.WaitPost(() => request = cSystem.Save(0, "  My mark  ", CustomMarkingPlacement.Skin, art));
        await WaitFor(pair, () => answer != null, "the server to answer the save");
        Assert.Multiple(() =>
        {
            Assert.That(answer.Request, Is.EqualTo(request));
            Assert.That(answer.Error, Is.Null);
            Assert.That(answer.Entry?.Name, Is.EqualTo("My mark"));
            Assert.That(answer.Entry?.Hash, Is.EqualTo(hash));
            Assert.That(answer.Entry?.Placement, Is.EqualTo(CustomMarkingPlacement.Skin));
        });
        await WaitFor(pair, () => cSystem.Library is { Count: 1 }, "the library to reach the client");
        Assert.That(cSystem.Library![0], Is.EqualTo(answer.Entry));

        // Saves are spaced out.
        var saved = answer.Entry;
        answer = null;
        await client.WaitPost(() => cSystem.Save(saved!.Value.Id, "Renamed", CustomMarkingPlacement.Hair, null));
        await WaitFor(pair, () => answer != null, "the server to answer the hasty save");
        Assert.That(answer.Error, Is.EqualTo("wf-custom-marking-error-cooldown"));
        await client.WaitPost(() => cSystem.SaveAnswered -= OnAnswer);

        // A body wearing it: the client fetches the art by hash and draws it.
        var testMap = await pair.CreateTestMap();
        EntityUid body = default;
        await server.WaitPost(() =>
        {
            body = server.EntMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithCustomMarkings(new List<CustomMarking> { new(hash, CustomMarkingPlacement.Skin) });
            server.EntMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(body, profile);
            server.PlayerMan.SetAttachedEntity(pair.Player!, body);
        });

        await pair.RunTicksSync(5);
        var clientBody = pair.ToClientUid(body);
        var sprites = client.System<SpriteSystem>();
        await WaitFor(pair, () => HasLayer(), "the client to draw the marking");
        await client.WaitAssertion(() =>
        {
            Assert.That(cSystem.TryGetPng(hash, out var png), Is.True);
            Assert.That(Content.Client._WF.CustomMarkings.CustomMarkingPng.Read(png).SamePixels(art), Is.True, "the art arrives as it was drawn");
        });

        // An admin blocks the art: it comes off the body and the client forgets it.
        var blocked = false;
        await server.WaitPost(async () => blocked = await sSystem.SetBlocked(hash, true, "the test"));
        await WaitFor(pair, () => blocked, "the block to be saved");
        await WaitFor(pair, () => !HasLayer(), "the client to stop drawing blocked art");
        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.GetComponent<HumanoidAppearanceComponent>(body).CustomMarkings, Is.Empty));
        await client.WaitAssertion(() => Assert.That(cSystem.TryGetArt(hash, out _), Is.False));

        await pair.CleanReturnAsync();
        return;

        bool HasLayer()
        {
            return client.EntMan.TryGetComponent(clientBody, out SpriteComponent sprite)
                   && sprites.LayerMapTryGet((clientBody, sprite), ClientCustomMarkingSystem.LayerKey(0), out _, false);
        }
    }

    private static async Task WaitFor(TestPair pair, Func<bool> condition, string what)
    {
        for (var i = 0; i < MaxWaitTicks; i++)
        {
            if (condition())
                return;

            await pair.RunTicksSync(1);
        }

        Assert.That(condition(), Is.True, $"Timed out waiting for {what}.");
    }

    private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        return new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);
    }
}
