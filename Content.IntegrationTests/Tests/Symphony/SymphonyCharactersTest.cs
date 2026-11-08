using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using Content.IntegrationTests.Pair;
using Content.Server.Preferences.Managers;
using Content.Server.Symphony;
using Content.Shared.Preferences;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Symphony;

/// <summary>The web creator's endpoints: a player's characters round-trip through JSON, saves are checked, and the catalog draws.</summary>
[TestFixture]
[TestOf(typeof(SymphonyCharactersSystem))]
public sealed class SymphonyCharactersTest
{
    [Test]
    public async Task SaveRoundTripTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var user = pair.Player!.UserId;
        var system = server.System<SymphonyCharactersSystem>();

        var list = await Run(pair, () => system.List(user));
        Assert.That(list.Code, Is.EqualTo(HttpStatusCode.OK), list.Body.ToJsonString());
        var slot = list.Body["slots"]!.AsArray()[0]!;
        var slotIndex = slot["slot"]!.GetValue<int>();
        var etag = slot["etag"]!.GetValue<string>();
        var profile = slot["profile"]!.AsObject();
        Assert.That(profile["name"], Is.Not.Null, "profiles use the serializer's camelCase field names");
        var bank = profile["bankBalance"]!.GetValue<string>();

        var edited = (JsonObject) profile.DeepClone();
        edited["name"] = "Web Made";
        edited["bankBalance"] = "987654321";

        var save = await Run(pair, () => system.Save(user, slotIndex, new JsonObject { ["etag"] = etag, ["profile"] = edited }));
        Assert.That(save.Code, Is.EqualTo(HttpStatusCode.OK), save.Body.ToJsonString());
        Assert.Multiple(() =>
        {
            Assert.That(save.Body["profile"]!["name"]!.GetValue<string>(), Is.EqualTo("Web Made"));
            Assert.That(save.Body["profile"]!["bankBalance"]!.GetValue<string>(), Is.EqualTo(bank), "a web save can't touch the bank");
            Assert.That(save.Body["etag"]!.GetValue<string>(), Is.Not.EqualTo(etag));
            Assert.That(save.Body["kept"]!.AsArray().Select(k => k!.GetValue<string>()), Does.Contain("bankBalance"));
        });

        await server.WaitAssertion(() =>
        {
            var live = server.ResolveDependency<IServerPreferencesManager>().GetPreferences(user).Characters[slotIndex];
            Assert.That(((HumanoidCharacterProfile) live).Name, Is.EqualTo("Web Made"), "a connected player's preferences change at once");
        });

        // The first etag is spent: saving with it again is refused and hands back what is there now.
        var stale = await Run(pair, () => system.Save(user, slotIndex, new JsonObject { ["etag"] = etag, ["profile"] = edited.DeepClone() }));
        Assert.That(stale.Code, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(stale.Body["current"]!["etag"]!.GetValue<string>(), Is.EqualTo(save.Body["etag"]!.GetValue<string>()));

        var badSlot = await Run(pair, () => system.Save(user, 9999, new JsonObject { ["etag"] = null, ["profile"] = edited.DeepClone() }));
        Assert.That(badSlot.Code, Is.EqualTo(HttpStatusCode.BadRequest));

        var junk = await Run(pair, () => system.Save(user, slotIndex, new JsonObject { ["etag"] = etag, ["profile"] = "nope" }));
        Assert.That(junk.Code, Is.EqualTo(HttpStatusCode.BadRequest));

        var validated = await Run(pair, () => system.Validate(user, new JsonObject { ["profile"] = edited.DeepClone() }));
        Assert.That(validated.Code, Is.EqualTo(HttpStatusCode.OK), validated.Body.ToJsonString());

        var stranger = await Run(pair, () => system.List(new NetUserId(Guid.NewGuid())));
        Assert.That(stranger.Code, Is.EqualTo(HttpStatusCode.NotFound), "an account that never played has nothing to edit");

        // Art the server doesn't hold is taken off on save.
        var bogus = new string('e', 64);
        var withArt = (JsonObject) save.Body["profile"]!.DeepClone();
        withArt["customMarkings"] = new JsonArray(new JsonObject { ["hash"] = bogus, ["placement"] = "Skin" });
        var artSave = await Run(pair, () => system.Save(user, slotIndex, new JsonObject { ["etag"] = save.Body["etag"]!.GetValue<string>(), ["profile"] = withArt }));
        Assert.That(artSave.Code, Is.EqualTo(HttpStatusCode.OK), artSave.Body.ToJsonString());
        Assert.That(artSave.Body["dropped_art"]!.AsArray().Select(h => h!.GetValue<string>()), Does.Contain(bogus));
        Assert.That(artSave.Body["profile"]!["customMarkings"]!.AsArray(), Is.Empty);

        // An offline account saves through the offline path: stored play times, straight to the database.
        var offline = new NetUserId(Guid.NewGuid());
        await pair.Server.ResolveDependency<Content.Server.Database.IServerDbManager>().InitPrefsAsync(offline, HumanoidCharacterProfile.DefaultWithSpecies(), default);
        var offlineList = await Run(pair, () => system.List(offline));
        Assert.That(offlineList.Body["online"]!.GetValue<bool>(), Is.False);
        var offlineSlot = offlineList.Body["slots"]!.AsArray()[0]!;
        var offlineProfile = (JsonObject) offlineSlot["profile"]!.DeepClone();
        offlineProfile["name"] = "Offline Edit";
        var offlineSave = await Run(pair, () => system.Save(offline, offlineSlot["slot"]!.GetValue<int>(),
            new JsonObject { ["etag"] = offlineSlot["etag"]!.GetValue<string>(), ["profile"] = offlineProfile }));
        Assert.That(offlineSave.Code, Is.EqualTo(HttpStatusCode.OK), offlineSave.Body.ToJsonString());
        Assert.That(offlineSave.Body["profile"]!["name"]!.GetValue<string>(), Is.EqualTo("Offline Edit"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CatalogTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var catalog = server.System<SymphonyCreatorSystem>().Build();
            var json = JsonNode.Parse(catalog.Json)!;

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Version, Has.Length.EqualTo(16));
                Assert.That(json["version"]!.GetValue<string>(), Is.EqualTo(catalog.Version));
                Assert.That(json["prototypes"]!["species"]!["Human"], Is.Not.Null);
                Assert.That(json["prototypes"]!["marking"]!.AsObject().Count, Is.GreaterThan(0));
                Assert.That(json["dolls"]!["Human"]!["layers"]!.AsArray().Count, Is.GreaterThan(5), json["prototypes"]!["entity"]![json["dolls"]!["Human"]!["prototype"]!.GetValue<string>()]?.ToJsonString());
                Assert.That(json["names"]!["species"]!["Human"]!.GetValue<string>(), Is.Not.Empty);
                Assert.That(catalog.Rsis, Is.Not.Empty);
                Assert.That(json["prototypes"]!["job"]!.AsObject().Count, Is.GreaterThan(0));
                Assert.That(json["prototypes"]!["department"]!.AsObject().Count, Is.GreaterThan(0));
                Assert.That(json["prototypes"]!["traitCategory"]!.AsObject().Count, Is.GreaterThan(0));
                Assert.That(json["prototypes"]!["dataset"]!.AsObject().Count + json["prototypes"]!["localizedDataset"]!.AsObject().Count,
                    Is.GreaterThan(0), "the species name lists, for randomise");
            });

            // The doll keeps its inventory, which carries the species' displacement maps.
            var doll = json["prototypes"]!["entity"]![json["dolls"]!["Human"]!["prototype"]!.GetValue<string>()]!;
            Assert.That(doll["components"]!.AsArray().Any(c => c!["type"]!.GetValue<string>() == "Inventory"), Is.True);

            // The species mob carries the default voice and typing bubble.
            var mob = json["prototypes"]!["entity"]![json["dolls"]!["Human"]!["mob"]!.GetValue<string>()]!;
            Assert.That(mob["components"]!.AsArray().Any(c => c!["type"]!.GetValue<string>() == "Vocal"), Is.True);
            Assert.That(json["names"]!["department"]!.AsObject().Count, Is.GreaterThan(0));

            // Every RSI it names is one the game has, so the web can fetch it.
            var resources = server.ResolveDependency<IResourceManager>();
            var missing = catalog.Rsis.Where(r => !resources.ContentFileExists(new ResPath("/Textures") / r / "meta.json")).ToList();
            Assert.That(missing, Is.Empty, string.Join(", ", missing.Take(20)));

            Assert.That(server.System<SymphonyCreatorSystem>().Build(), Is.SameAs(catalog), "built once, then served from memory");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Starts an endpoint's work on the main thread and ticks the server until it is answered.</summary>
    private static async Task<SymphonyCharactersSystem.Answer> Run(TestPair pair, Func<Task<SymphonyCharactersSystem.Answer>> work)
    {
        Task<SymphonyCharactersSystem.Answer> task = null;
        await pair.Server.WaitPost(() => task = work());
        for (var i = 0; i < 600 && !task!.IsCompleted; i++)
        {
            await pair.Server.WaitRunTicks(1);
        }

        Assert.That(task!.IsCompleted, Is.True, "the endpoint answered");
        return await task;
    }
}
