#nullable enable
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Monitor.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Power.Components;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.DeviceNetwork.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.DeviceNetGuard;

/// <summary>
///     Two linked air alarms that set different modes in the same tick settle. They used to echo each other's mode
///     back every tick forever, flipping every vent and scrubber on the network (the 2026-11-11 packet storms).
/// </summary>
[TestFixture]
[TestOf(typeof(AirAlarmSystem))]
public sealed class AirAlarmModeEchoTest
{
    [Test]
    public async Task ConflictingModesSettle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var alarms = server.System<AirAlarmSystem>();
        var deviceNet = server.System<DeviceNetworkSystem>();

        EntityUid a = default, b = default;
        await server.WaitPost(() =>
        {
            a = entities.SpawnEntity("AirAlarm", map.GridCoords);
            b = entities.SpawnEntity("AirAlarm", map.GridCoords);
            foreach (var alarm in new[] { a, b })
            {
                entities.GetComponent<ApcPowerReceiverComponent>(alarm).NeedsPower = false;
                entities.GetComponent<AirAlarmComponent>(alarm).AutoMode = false;
            }

            // Alarms broadcast only to the devices linked to them, as mappers link a ship's rooms.
            var lists = entities.System<DeviceListSystem>();
            lists.UpdateDeviceList(a, new[] { b });
            lists.UpdateDeviceList(b, new[] { a });
        });
        await pair.RunTicksSync(10);

        await server.WaitPost(() =>
        {
            // Two rooms disagree in the same tick, as auto mode does when one goes Danger and another Normal.
            alarms.SetMode(a, entities.GetComponent<DeviceNetworkComponent>(a).Address, AirAlarmMode.WideFiltering, false);
            alarms.SetMode(b, entities.GetComponent<DeviceNetworkComponent>(b).Address, AirAlarmMode.Panic, false);
        });

        await pair.RunTicksSync(5);
        (AirAlarmMode, AirAlarmMode) Modes() => (entities.GetComponent<AirAlarmComponent>(a).CurrentMode,
            entities.GetComponent<AirAlarmComponent>(b).CurrentMode);

        var settled = (AirAlarmMode.None, AirAlarmMode.None);
        await server.WaitAssertion(() =>
        {
            settled = Modes();
            Assert.That(settled, Is.Not.EqualTo((AirAlarmMode.WideFiltering, AirAlarmMode.Panic)),
                "Neither alarm heard the other's mode.");
        });

        for (var tick = 0; tick < 10; tick++)
        {
            await pair.RunTicksSync(1);
            await server.WaitAssertion(() =>
            {
                Assert.That(Modes(), Is.EqualTo(settled), "The alarms kept echoing modes back and forth.");
                Assert.That(deviceNet.Backlog, Is.Zero, "Mode packets are still going round.");
            });
        }

        await pair.CleanReturnAsync();
    }
}
