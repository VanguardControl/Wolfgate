#nullable enable
using Content.Shared.Sound;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Audio;

/// <summary>The landing-sound budget counts sounds, not landings.</summary>
[TestFixture]
[TestOf(typeof(SharedEmitSoundSystem))]
public sealed class LandSoundBudgetTest
{
    /// <summary>Any number of silent landings leaves the budget whole, so the next landing that makes a sound is heard.</summary>
    [Test]
    public async Task SilentLandingsSpendNothing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var before = 0;
        var after = 0;

        await server.WaitPost(() =>
        {
            var item = entMan.SpawnEntity("Crowbar", map.GridCoords);

            for (var i = 0; i < 40; i++)
            {
                var silent = new LandEvent(null, false);
                entMan.EventBus.RaiseLocalEvent(item, ref silent);
            }

            before = entMan.Count<AudioComponent>();
            var loud = new LandEvent(null, true);
            entMan.EventBus.RaiseLocalEvent(item, ref loud);
            after = entMan.Count<AudioComponent>();
        });

        Assert.That(after, Is.EqualTo(before + 1), "A landing made no sound after silent landings spent the budget.");

        await pair.CleanReturnAsync();
    }
}
