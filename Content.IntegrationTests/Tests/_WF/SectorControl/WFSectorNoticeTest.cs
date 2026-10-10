#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Notices: a crewed ship inside held space for a minute is told so, once, in chat where it has no speaker.</summary>
[TestOf(typeof(WFSectorNoticeSystem))]
public sealed class WFSectorNoticeTest : WFSectorTestBase
{
    private static readonly WFSectorCell Cell = new(4, -2);

    [Test]
    public async Task ShipInsideHeldSpaceIsToldAfterAMinute()
    {
        await StartRound();
        var chat = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        EntityUid ship = default;
        TimeSpan entered = default;
        await Server.WaitAssertion(() =>
        {
            ship = SpawnGrid(CentreOf(Cell), "Test Hauler");
            Board(ship);
            Assert.That(Server.System<WFSectorTerritorySystem>().TryClaim(MapData.MapId, Cell, Holder, null), Is.True);
            Server.System<WFSectorNoticeSystem>().Check();
            entered = STiming.CurTime;
        });

        await RunTicks(120);
        Assert.That(chat.History.Any(entry => entry.Msg.Message.Contains("Test Hauler")), Is.False, "Not before the minute is up.");

        await WaitUntilServer(() => STiming.CurTime - entered > TimeSpan.FromSeconds(66), 6000);
        await RunTicks(60);
        Assert.That(chat.History.Count(entry => entry.Msg.Message.Contains("Test Hauler") && entry.Msg.Message.Contains("Faction id")
                                                && entry.Msg.Message.Contains(WFSectorHex.Callsign(Cell))), Is.EqualTo(1),
            "Told once that it has entered held space, with the cell.");

        await Server.WaitAssertion(() =>
        {
            // Another look straight away tells it nothing more.
            Server.System<WFSectorNoticeSystem>().Check();
        });
        await RunTicks(60);
        Assert.That(chat.History.Count(entry => entry.Msg.Message.Contains("Test Hauler")), Is.EqualTo(1));
    }
}
