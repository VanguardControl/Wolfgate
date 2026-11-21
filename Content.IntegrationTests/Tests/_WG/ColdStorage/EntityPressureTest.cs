#nullable enable
using Content.IntegrationTests.Pair;
using Content.Server._Mono.Cleanup;
using Content.Server._WG.ColdStorage;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ColdStorage;

/// <summary>
/// Entity pressure rises from 0 to 1 between the low and high counts, moves in small steps, and tightens stasis and
/// cleanup only while it is above 0.
/// </summary>
[TestFixture]
[TestOf(typeof(WGEntityPressureSystem))]
public sealed class EntityPressureTest
{
    private const float Epsilon = 0.0001f;

    /// <summary>Pressure is the count's place between low and high, 0 when disabled, and Scale lerps by it.</summary>
    [Test]
    public async Task PressureFollowsTheEntityCount()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var pressure = server.System<WGEntityPressureSystem>();

        try
        {
            await server.WaitPost(() =>
            {
                var count = server.EntMan.EntityCount;

                Set(pair, true, count - 100, count + 100);
                pressure.SampleNow(true);
                Assert.That(pressure.Pressure, Is.EqualTo(0.5f).Within(Epsilon), "Halfway between low and high.");
                Assert.That(pressure.Scale(1f, 0.25f), Is.EqualTo(0.625f).Within(Epsilon));

                Set(pair, true, -1, 0);
                pressure.SampleNow(true);
                Assert.That(pressure.Pressure, Is.EqualTo(1f), "At or above high.");
                Assert.That(pressure.Scale(1f, 0.25f), Is.EqualTo(0.25f).Within(Epsilon));

                Set(pair, true, count + 100, count + 200);
                pressure.SampleNow(true);
                Assert.That(pressure.Pressure, Is.EqualTo(0f), "At or below low.");
                Assert.That(pressure.Scale(1f, 0.25f), Is.EqualTo(1f), "A relaxed server is untouched.");

                Set(pair, false, -1, 0);
                pressure.SampleNow(true);
                Assert.That(pressure.Pressure, Is.EqualTo(0f), "Disabled is always 0.");
            });
        }
        finally
        {
            await server.WaitPost(() => Reset(pair, pressure));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Without a snap, pressure moves at most 0.02 a sample; the event fires on a 0.05 move and on return to 0.</summary>
    [Test]
    public async Task SmoothingLimitsTheStepAndTheEventFollowsTheMove()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var pressure = server.System<WGEntityPressureSystem>();

        try
        {
            await server.WaitPost(() =>
            {
                Set(pair, true, -1, 0);
                pressure.SampleNow(true);
                Set(pair, false, -1, 0);
                pressure.SampleNow(true);
                Set(pair, true, -1, 0);
                var raised = pressure.Raised;

                pressure.SampleNow(false);
                Assert.That(pressure.Pressure, Is.EqualTo(WGEntityPressureSystem.MaxStep).Within(Epsilon), "One sample moves one step.");
                pressure.SampleNow(false);
                Assert.That(pressure.Pressure, Is.EqualTo(0.04f).Within(Epsilon));
                Assert.That(pressure.Raised, Is.EqualTo(raised), "Under 0.05 since the last raise.");

                pressure.SampleNow(false);
                Assert.That(pressure.Pressure, Is.EqualTo(0.06f).Within(Epsilon));
                Assert.That(pressure.Raised, Is.EqualTo(raised + 1), "0.05 or more since the last raise.");

                Set(pair, false, -1, 0);
                pressure.SampleNow(false);
                Assert.That(pressure.Pressure, Is.EqualTo(0f), "Disabling drops it to 0 at once.");
                Assert.That(pressure.Raised, Is.EqualTo(raised + 2), "Returning to 0 raises the event.");
            });
        }
        finally
        {
            await server.WaitPost(() => Reset(pair, pressure));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>At pressure 1 stasis waits a quarter as long and cleanup radii and delay halve; at 0 they are untouched.</summary>
    [Test]
    public async Task ConsumersTightenOnlyUnderPressure()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var pressure = server.System<WGEntityPressureSystem>();
        var stasis = server.System<WGGridStasisSystem>();
        var space = server.System<SpaceCleanupSystem>();
        var grids = server.System<GridCleanupSystem>();
        var idle = 0f;

        try
        {
            await server.WaitPost(() =>
            {
                idle = server.CfgMan.GetCVar(WGStasisCVars.Idle);

                Set(pair, true, -1, 0);
                pressure.SampleNow(true);
                Assert.Multiple(() =>
                {
                    Assert.That(stasis.EffectiveIdle, Is.EqualTo(idle * 0.25f).Within(Epsilon));
                    Assert.That(space.WGPressureAggression(1f), Is.EqualTo(0.5f).Within(Epsilon));
                    Assert.That(grids.WGRadiusScale, Is.EqualTo(0.5f).Within(Epsilon));
                    Assert.That(grids.WGDurationScale, Is.EqualTo(0.5f).Within(Epsilon));
                });

                Set(pair, false, -1, 0);
                pressure.SampleNow(true);
                Assert.Multiple(() =>
                {
                    Assert.That(stasis.EffectiveIdle, Is.EqualTo(idle));
                    Assert.That(space.WGPressureAggression(1f), Is.EqualTo(1f));
                    Assert.That(grids.WGRadiusScale, Is.EqualTo(1f));
                    Assert.That(grids.WGDurationScale, Is.EqualTo(1f));
                });
            });
        }
        finally
        {
            await server.WaitPost(() => Reset(pair, pressure));
        }

        await pair.CleanReturnAsync();
    }

    private static void Set(TestPair pair, bool enabled, int low, int high)
    {
        var cfg = pair.Server.CfgMan;
        cfg.SetCVar(WGPressureCVars.Low, low);
        cfg.SetCVar(WGPressureCVars.High, high);
        cfg.SetCVar(WGPressureCVars.Enabled, enabled);
    }

    private static void Reset(TestPair pair, WGEntityPressureSystem pressure)
    {
        var cfg = pair.Server.CfgMan;
        cfg.SetCVar(WGPressureCVars.Enabled, false);
        cfg.SetCVar(WGPressureCVars.Low, 500000);
        cfg.SetCVar(WGPressureCVars.High, 800000);
        pressure.SampleNow(true);
    }
}
