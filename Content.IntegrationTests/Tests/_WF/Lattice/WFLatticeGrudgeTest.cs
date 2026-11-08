using System.Linq;
using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.Remade;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>
/// An anchor station names a player aboard it, never forgives whoever struck it (even off its deck, with its shield up),
/// and a Scion slows whoever drags it.
/// </summary>
public sealed class WFLatticeGrudgeTest
{
    [Test]
    public async Task TrespassGrudgeAndHeavyScion()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var lattice = entities.System<WFLatticeSystem>();
        var maps = entities.System<SharedMapSystem>();
        var xforms = entities.System<SharedTransformSystem>();

        await server.WaitPost(() => lattice.Setup(map.MapUid));
        await pair.RunSeconds(2f);

        EntityUid station = default, mob = default;
        await server.WaitPost(() =>
        {
            station = entities.EntityQuery<WFLatticeAnchorStationComponent>().First().Owner;
            var core = entities.GetComponent<WFLatticeAnchorStationComponent>(station).Core;
            var at = entities.GetComponent<TransformComponent>(core).Coordinates.Offset(new Vector2(2f, 0f));
            mob = entities.SpawnEntity("MobHuman", at);
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        await pair.RunSeconds(2f);
        Entity<MapGridComponent> ship = default;
        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<WFLatticeAnchorStationComponent>(station);
            Assert.That(comp.Trespassers.ContainsKey(mob), Is.True, "a player aboard an anchor station wasn't named.");

            lattice.Provoke(station, mob);
            Assert.That(comp.Grudges, Does.Contain(mob), "the station forgave whoever struck it.");

            // Gone to their ship, the provocation long over and the shield up: the station still fires.
            ship = maps.CreateGridEntity(map.MapId);
            var stationAt = xforms.GetWorldPosition(station);
            xforms.SetWorldPosition(ship.Owner, stationAt + Vector2.Normalize(stationAt) * 800f);
            maps.SetTile(ship.Owner, ship.Comp, Vector2i.Zero, map.Tile.Tile);
            xforms.SetCoordinates(mob, new EntityCoordinates(ship.Owner, new Vector2(0.5f, 0.5f)));
            comp.Hostiles.Clear();
            comp.ProvokedUntil = TimeSpan.Zero;
            comp.Engaged = false;
        });

        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<WFLatticeAnchorStationComponent>(station);
            Assert.That(lattice.ShieldDown(station), Is.False);
            Assert.That(comp.Engaged, Is.True, "the station let a grudge go with its shield up.");

            // A Scion is a dead weight to drag.
            var scion = entities.SpawnEntity("WFMonolithScion", new EntityCoordinates(ship.Owner, new Vector2(0.5f, 0.5f)));
            Assert.That(entities.System<PullingSystem>().TryStartPull(mob, scion), Is.True);
            Assert.That(entities.TryGetComponent<WFHeavyPullingComponent>(mob, out var heavy) && heavy.Multiplier < 0.5f, Is.True,
                "dragging a Scion didn't slow its puller.");
            entities.System<PullingSystem>().TryStopPull(scion, entities.GetComponent<Content.Shared.Movement.Pulling.Components.PullableComponent>(scion));
            Assert.That(entities.HasComponent<WFHeavyPullingComponent>(mob), Is.False);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });

        await pair.CleanReturnAsync();
    }
}
