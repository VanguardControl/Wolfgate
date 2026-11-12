#nullable enable
using Content.Server._WF.NetTraffic;
using Content.Server.Power.EntitySystems;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Silicons.Borgs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.NetTraffic;

/// <summary>An entity nobody has open holds no UI state, and a draining cell doesn't make a cyborg send its states again.</summary>
[TestFixture]
[TestOf(typeof(WFUiStateTrimSystem))]
public sealed class WFUiStateTrimTest
{
    private const string Borg = "BorgChassisMedical";
    private const string Cell = "PowerCellHigh";
    private const string Canister = "AirCanister";

    [Test]
    public async Task DrainingCellDoesNotResendBorgUi()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var containers = server.System<SharedContainerSystem>();
        var battery = server.System<BatterySystem>();
        var ui = server.System<UserInterfaceSystem>();
        EntityUid borg = default;
        EntityUid cell = default;

        await server.WaitAssertion(() =>
        {
            borg = entMan.SpawnEntity(Borg, map.GridCoords);
            cell = entMan.SpawnEntity(Cell, map.GridCoords);
            // Straight into the container: the chassis is locked, so the slot refuses an insert.
            Assert.That(containers.Insert(cell, containers.GetContainer(borg, "cell_slot"), force: true));
        });

        await pair.RunTicksSync(5);
        var before = entMan.GetComponent<UserInterfaceComponent>(borg).LastModifiedTick;

        for (var i = 0; i < 10; i++)
        {
            await server.WaitAssertion(() => Assert.That(battery.TryUseCharge(cell, 0.5f), "The cell has charge to drain."));
            await pair.RunTicksSync(1);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<UserInterfaceComponent>(borg).LastModifiedTick, Is.EqualTo(before),
                "A charge change with the cyborg's window closed sent its UI states again.");
            Assert.That(ui.TryGetUiState<NavBoundUserInterfaceState>(borg, RadarConsoleUiKey.Key, out _), Is.False,
                "A cyborg nobody is using as a radar holds a nav state.");
            Assert.That(ui.TryGetUiState<BorgBuiState>(borg, BorgUiKey.Key, out _), Is.False,
                "A cyborg whose window was never opened holds a state for it.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClosedCanisterHoldsNoUiState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();
        EntityUid canister = default;

        await server.WaitPost(() => canister = entMan.SpawnEntity(Canister, map.GridCoords));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
            Assert.That(ui.TryGetUiState<GasCanisterBoundUserInterfaceState>(canister, GasCanisterUiKey.Key, out _), Is.False,
                "A canister nobody has open holds a UI state."));

        await pair.CleanReturnAsync();
    }
}
