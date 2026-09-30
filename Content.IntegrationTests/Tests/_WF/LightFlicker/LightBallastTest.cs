using System.Diagnostics;
using System.Numerics;
using Content.Client._WF.LightFlicker;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.Explosion;
using Content.Shared._WF.LightFlicker;
using Content.Shared.Light;
using Content.Shared.Power.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.LightFlicker;

// An explosion's shockwave damages light ballasts in its reach, and a multitool repairs them.
public sealed class LightBallastTest : InteractionTest
{
    private const string Light = "Poweredlight";

    [Test]
    public async Task ShockwaveDamagesLightsInReach()
    {
        var light = ToServer(await SpawnTarget(Light));
        var far = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            var pos = Transform.GetMapCoordinates(light);
            far = SEntMan.SpawnEntity(Light, new MapCoordinates(pos.Position + new Vector2(40f, 0f), pos.MapId));

            var ev = new ExplosionShockwaveEvent(new MapCoordinates(pos.Position - new Vector2(1f, 0f), pos.MapId), 2, null);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<DamagedBallastComponent>(light), Is.True,
                "A light inside the shockwave should have its ballast damaged.");
            Assert.That(SEntMan.HasComponent<DamagedBallastComponent>(far), Is.False,
                "A light well past the shockwave should be left alone.");
        });
    }

    [Test]
    public async Task MultitoolRepairsBallast()
    {
        var light = ToServer(await SpawnTarget(Light));

        await Server.WaitPost(() => SEntMan.System<LightBallastSystem>().DamageBallast(light));
        await RunTicks(5);

        Assert.That(CEntMan.HasComponent<DamagedBallastComponent>(ToClient(light)), Is.True,
            "Test setup: the damaged ballast should reach the client.");

        await InteractUsing("Multitool");

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<DamagedBallastComponent>(light), Is.False,
                "A multitool should repair the ballast."));
    }

    // The client strikes a light on with a stutter, flickers it while the ballast is damaged and lights it once repaired.
    [Test]
    public async Task ClientFlickersLight()
    {
        var light = ToServer(await SpawnTarget(Light));
        var client = ToClient(light);

        // A light only plays its turn-on sound, which the strike keys off, once the round is two seconds old.
        await RunTicks(100);

        // No APC on the test map, so let the light run unpowered.
        await Server.WaitPost(() => SEntMan.System<SharedPowerReceiverSystem>().SetNeedsPower(light, false));

        var sawStrike = await WaitForClient(() => Mode(client) == LightFlickerMode.Strike, 3);
        Assert.That(sawStrike, Is.True, "A light switching on should strike with a stutter.");
        Assert.That(await WaitForClient(() => Mode(client) == LightFlickerMode.None && Shows(client, true), 3), Is.True,
            "The strike should end with the light steadily lit.");

        await Server.WaitPost(() => SEntMan.System<LightBallastSystem>().DamageBallast(light));

        Assert.That(await WaitForClient(() => Mode(client) == LightFlickerMode.Fault && Shows(client, false), 3), Is.True,
            "A damaged ballast should start the fault flicker, dark first.");
        Assert.That(await WaitForClient(() => Shows(client, true), 5), Is.True,
            "A faulty light should strike back on within a cycle.");
        Assert.That(await WaitForClient(() => Shows(client, false), 8), Is.True,
            "A faulty light should go dark again.");

        await InteractUsing("Multitool");

        Assert.That(await WaitForClient(() => Mode(client) == LightFlickerMode.None && Shows(client, true), 3), Is.True,
            "A repaired light should stop flickering and stay lit.");
    }

    /// <summary>Whether both the light and the fixture's glow sprite are in the given state.</summary>
    private bool Shows(EntityUid client, bool lit)
    {
        var sprite = CEntMan.GetComponent<SpriteComponent>(client);
        var sprites = CEntMan.System<SpriteSystem>();

        Assert.That(sprites.LayerMapTryGet((client, sprite), PoweredLightLayers.Glow, out var glow, false), Is.True,
            "Test setup: the light should have a glow layer.");

        return Lit(client) == lit && sprite[glow].Visible == lit;
    }

    private LightFlickerMode Mode(EntityUid client)
    {
        return CEntMan.TryGetComponent<LightFlickerComponent>(client, out var flicker)
            ? flicker.Mode
            : LightFlickerMode.None;
    }

    private bool Lit(EntityUid client)
    {
        return CEntMan.GetComponent<PointLightComponent>(client).Enabled;
    }

    /// <summary>
    /// Polls a client-side condition against wall-clock time, since the flicker runs on real time and ticks outrun it.
    /// </summary>
    private async Task<bool> WaitForClient(Func<bool> condition, double seconds)
    {
        var watch = Stopwatch.StartNew();

        while (watch.Elapsed.TotalSeconds < seconds)
        {
            var met = false;
            await Client.WaitPost(() => met = condition());

            if (met)
                return true;

            await RunTicks(1);
            await Task.Delay(10);
        }

        return false;
    }
}
