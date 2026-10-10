using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Server._WF.Shuttles.Systems;
using Content.Server.Construction;
using Content.Server.Construction.Completions;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>Distinguishes completed structural deconstruction from damage and cancelled construction.</summary>
public sealed class ShipHullConstructionTest
{
    [Test]
    public async Task NativeConstructionAndRcdCompletionRemoveOnlyIntentionalHullCapacity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var status = em.System<ShipStatusSystem>();
        EntityUid console = default;
        EntityUid changedWall = default;
        EntityUid rcdWall = default;
        await pair.Server.WaitAssertion(() =>
        {
            console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            foreach (var prototype in new[] { "WallSolid", "Window", "Airlock" })
            {
                var structure = em.SpawnEntity(prototype, map.GridCoords);
                Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
                new DeleteEntity().PerformAction(structure, null, em);
                Assert.That(em.Deleted(structure), Is.True);
                Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                    $"Completed native deletion of {prototype} must leave healthy floor, not permanent structural damage.");
            }
            changedWall = em.SpawnEntity("WallSolid", map.GridCoords);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            Assert.That(em.System<ConstructionSystem>().ChangeNode(changedWall, null, "girder"), Is.True);
        });
        await pair.Server.WaitRunTicks(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(changedWall), Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "Replacing a wall with its girder through the real construction graph must revise its design capacity.");
            rcdWall = em.SpawnEntity("WallSolid", map.GridCoords);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            var rcd = em.SpawnEntity("RCD", map.GridCoords);
            var component = em.GetComponent<RCDComponent>(rcd);
            var prototype = pair.Server.ProtoMan.Index<RCDPrototype>("Deconstruct");
            typeof(RCDComponent).GetProperty(nameof(RCDComponent.CachedPrototype))!.SetValue(component, prototype);
            var data = new MapGridData(map.Grid.Owner, map.Grid.Comp, map.GridCoords, map.Tile, Vector2i.Zero);
            // Exercise the native successful-operation branch; tool range and do-after checks are independent.
            typeof(RCDSystem).GetMethod("FinalizeRCDOperation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(em.System<RCDSystem>(), new object[] { rcd, component, data, Direction.South, rcdWall, rcd });
        });
        await pair.Server.WaitRunTicks(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(rcdWall), Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "Successful RCD structural removal must use the same intentional-removal accounting.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CancelledAndUnsurveyedRemovalsCannotEraseCombatLoss()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var status = em.System<ShipStatusSystem>();
            var console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            var wall = em.SpawnEntity("WallSolid", map.GridCoords);
            var secondWall = em.SpawnEntity("WallSolid", map.GridCoords);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            var cancelled = new ConstructionBeforeDeleteEvent(null);
            em.EventBus.RaiseLocalEvent(wall, cancelled);
            cancelled.Cancel();
            em.DeleteEntity(wall);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(0.5f).Within(0.001f),
                "A cancelled construction event must not excuse subsequent destruction.");
            new DeleteEntity().PerformAction(secondWall, null, em);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.Zero,
                "Removing one surviving wall intentionally must retain the other wall's combat loss on the same tile.");
            var unsurveyed = em.SpawnEntity("WallSolid", map.GridCoords);
            new DeleteEntity().PerformAction(unsurveyed, null, em);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.Zero,
                "An entity built and removed between surveys must not consume historic capacity it never supplied.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeconstructionOnSplitFragmentDoesNotEditItsSiblingsSurvey()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var maps = em.System<SharedMapSystem>();
            var status = em.System<ShipStatusSystem>();
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 2; y++)
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);
            var left = new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f));
            var console = em.SpawnEntity("ComputerShuttle", left);
            var wall = em.SpawnEntity("WallSolid", left);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            pair.Server.CfgMan.SetCVar(CVars.GridSplitting, true);
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(1, 0), Tile.Empty),
                (new Vector2i(1, 1), Tile.Empty),
            });
            Assert.That(em.GetComponent<TransformComponent>(wall).GridUid, Is.Not.EqualTo(map.Grid.Owner));
            pair.Server.CfgMan.SetCVar(CVars.GridSplitting, false);
            new DeleteEntity().PerformAction(wall, null, em);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(0.2f).Within(0.001f),
                "Intentional removal before the fragment's first new survey must use its inherited location.");
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, Vector2i.Zero, map.Tile.Tile);
            var originalConsole = em.SpawnEntity("ComputerShuttle",
                new EntityCoordinates(map.Grid.Owner, new Vector2(2.5f, 0.5f)));
            Assert.That(status.GetStatus(originalConsole)!.Summary.HullIntegrity, Is.EqualTo(0.6f).Within(0.001f),
                "Restoring only the original grid's floor cannot restore the wall removed on its sibling fragment.");
        });
        await pair.CleanReturnAsync();
    }
}
