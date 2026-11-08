using System.Net;
using System.Text.Json.Nodes;
using Content.IntegrationTests.Pair;
using Content.Server.Symphony;
using Content.Shared._WF.AppearanceStudio;
using Content.Shared._WF.CustomMarkings;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.IntegrationTests.Tests.Symphony;

/// <summary>The web creator's custom marking library and warps go through the game's own checks.</summary>
[TestFixture]
[TestOf(typeof(SymphonyCustomMarkingsSystem))]
public sealed class SymphonyCustomMarkingsTest
{
    [Test]
    public async Task LibraryAndWarpTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var user = pair.Player!.UserId;
        var system = pair.Server.System<SymphonyCustomMarkingsSystem>();

        var empty = await Run(pair, () => system.List(user));
        Assert.That(empty.Code, Is.EqualTo(HttpStatusCode.OK), empty.Body.ToJsonString());
        Assert.That(empty.Body["entries"]!.AsArray(), Is.Empty);

        var art = new CustomMarkingArt();
        art.SetPixel(0, CustomMarkingArt.South, 10, 10, new Rgba32(255, 0, 0, 255));
        var body = new JsonObject
        {
            ["id"] = 0,
            ["name"] = "Web stripe",
            ["placement"] = "Skin",
            ["art"] = new JsonObject
            {
                ["pixels"] = Convert.ToBase64String(art.Pixels),
                ["frameTimes"] = new JsonArray(200),
                ["pingPong"] = false,
                ["erase"] = null,
            },
        };

        var saved = await Run(pair, () => system.Save(user, (JsonObject) body.DeepClone()));
        Assert.That(saved.Code, Is.EqualTo(HttpStatusCode.OK), saved.Body.ToJsonString());
        var entry = saved.Body["entry"]!;
        Assert.Multiple(() =>
        {
            Assert.That(entry["name"]!.GetValue<string>(), Is.EqualTo("Web stripe"));
            Assert.That(entry["placement"]!.GetValue<string>(), Is.EqualTo("Skin"));
            Assert.That(CustomMarkingRules.IsValidHash(entry["hash"]!.GetValue<string>()), Is.True, "the game hashed it");
            Assert.That(saved.Body["erase_dropped"]!.GetValue<bool>(), Is.False);
        });

        // Saving again at once trips the cooldown the game's editor shares.
        var hurried = await Run(pair, () => system.Save(user, (JsonObject) body.DeepClone()));
        Assert.That(hurried.Code, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(hurried.Body["error"]!.GetValue<string>(), Is.EqualTo("cooldown"));
        Assert.That(hurried.Body["cooldown_ms"]!.GetValue<int>(), Is.GreaterThan(0));

        var listed = await Run(pair, () => system.List(user));
        Assert.That(listed.Body["entries"]!.AsArray(), Has.Count.EqualTo(1));

        var badPlacement = (JsonObject) body.DeepClone();
        badPlacement["placement"] = "Elsewhere";
        var refused = await Run(pair, () => system.Save(user, badPlacement));
        Assert.That(refused.Code, Is.EqualTo(HttpStatusCode.BadRequest));

        var id = entry["id"]!.GetValue<int>();
        var deleted = await Run(pair, () => system.Delete(user, id));
        Assert.That(deleted.Code, Is.EqualTo(HttpStatusCode.NoContent));
        var gone = await Run(pair, () => system.Delete(user, id));
        Assert.That(gone.Code, Is.EqualTo(HttpStatusCode.NotFound));

        // Warps: one that changes nothing is refused, a real one comes back hashed.
        var neutral = await Run(pair, () => system.SaveWarp(user, new JsonObject { ["pixels"] = Convert.ToBase64String(new StudioWarp().ToArt().Pixels) }));
        Assert.That(neutral.Code, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(neutral.Body["error"]!.GetValue<string>(), Is.EqualTo("neutral"));

        var warp = new StudioWarp();
        warp.Set(CustomMarkingArt.South, 5, 5, 2, 0);
        var stored = await Run(pair, () => system.SaveWarp(user, new JsonObject { ["pixels"] = Convert.ToBase64String(warp.ToArt().Pixels) }));
        Assert.That(stored.Code, Is.EqualTo(HttpStatusCode.OK), stored.Body.ToJsonString());
        Assert.That(CustomMarkingRules.IsValidHash(stored.Body["hash"]!.GetValue<string>()), Is.True);

        await pair.CleanReturnAsync();
    }

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
