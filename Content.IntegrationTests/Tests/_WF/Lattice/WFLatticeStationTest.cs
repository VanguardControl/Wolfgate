using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Lattice;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.Remade.Ward;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>Anchor stations are built shielded, a destroyed core is a breach, and a parked relay takes hold.</summary>
public sealed class WFLatticeStationTest
{
    [Test]
    public async Task StationsBreachesAndRelays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var damage = entities.System<DamageableSystem>();

        var stations = new List<EntityUid>();
        EntityUid relay = default;
        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);
            var query = entities.EntityQueryEnumerator<WFLatticeAnchorStationComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid == map.MapUid)
                    stations.Add(uid);
            }

            Assert.That(stations, Has.Count.EqualTo(6));
            foreach (var station in stations)
            {
                var comp = entities.GetComponent<WFLatticeAnchorStationComponent>(station);
                Assert.That(entities.GetComponent<WFLatticeAnchorComponent>(comp.Core).Primary, Is.True);
                Assert.That(entities.GetComponent<PowerSupplierComponent>(comp.Core).MaxSupply, Is.GreaterThanOrEqualTo(100000f),
                    "an anchor core doesn't power its station.");
                Assert.That(entities.GetComponent<Content.Shared.NodeContainer.NodeContainerComponent>(comp.Core).Nodes.ContainsKey("output"), Is.True);
                var shielded = entities.EntityQuery<ShipShieldEmitterComponent>()
                    .Any(emitter => entities.GetComponent<TransformComponent>(emitter.Owner).GridUid == station);
                Assert.That(shielded, Is.True, "an anchor station has no shield generator.");
            }

            var core = entities.GetComponent<WFLatticeAnchorStationComponent>(stations[0]).Core;
            var blunt = new DamageSpecifier(protos.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(30000));
            damage.TryChangeDamage(core, blunt, ignoreResistances: true);

            relay = entities.SpawnEntity("WFLatticeRelayAnchor", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(relay).NeedsPower = false;
            entities.GetComponent<WFLatticeRelayComponent>(relay).SettleSeconds = 1f;
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            Assert.That(field.Integrity, Is.EqualTo(5f / 6f).Within(0.001f), "the breach didn't lower the Lattice's integrity.");
            Assert.That(entities.GetComponent<WFLatticeAnchorComponent>(relay).Active, Is.True, "the parked relay didn't take hold.");
            Assert.That(field.Anchors, Has.Count.EqualTo(6), "five primaries and the relay should hold the field.");

            // The standing stations' shields are up and drawn as a purple rooted Ward.
            var warded = entities.EntityQuery<ShipShieldEmitterComponent>()
                .Where(emitter => entities.HasComponent<WFLatticeAnchorShieldComponent>(emitter.Owner))
                .Count(emitter => emitter.Shield is { } shield
                                  && entities.TryGetComponent<WFWardFieldComponent>(shield, out var ward)
                                  && ward.Rooted && ward.Tint != null);
            Assert.That(warded, Is.GreaterThanOrEqualTo(5), "the anchor shields aren't up as purple Ward fields.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A few stray hits are forgiven; past the limit the station turns its lances on the attacker.</summary>
    [Test]
    public async Task StationsAnswerAttacks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var maps = entities.System<Robust.Shared.GameObjects.SharedMapSystem>();
        var transform = entities.System<SharedTransformSystem>();

        EntityUid station = default, attacker = default;
        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);
            var query = entities.EntityQueryEnumerator<WFLatticeAnchorStationComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapUid == map.MapUid)
                    station = uid;
            }

            var core = entities.GetComponent<WFLatticeAnchorStationComponent>(station).Core;
            Assert.That(entities.HasComponent<Content.Server.Shuttles.Components.StationAnchorComponent>(core), Is.True, "the core isn't a station anchor.");
            var lances = entities.EntityQuery<WFLatticeDefenceWeaponComponent>()
                .Count(lance => entities.GetComponent<TransformComponent>(lance.Owner).GridUid == station);
            Assert.That(lances, Is.EqualTo(8), "the loaded station should carry eight lances.");

            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId));
            transform.SetWorldPosition(grid.Owner, transform.GetWorldPosition(station) + new System.Numerics.Vector2(400f, 0f));
            attacker = grid.Owner;

            var comp = entities.GetComponent<WFLatticeAnchorStationComponent>(station);
            lattice.Strike(station, attacker, 1f);
            Assert.That(comp.Hostiles, Is.Empty, "one stray hit made the station hostile.");
            lattice.Strike(station, attacker, 1f);
            lattice.Strike(station, attacker, 1f);
            lattice.Strike(station, attacker, 1f);
            Assert.That(comp.Hostiles.ContainsKey(attacker), Is.True, "repeated hits didn't make the station hostile.");
        });

        await pair.RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            var bolts = entities.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == "WFLatticeLanceProjectile");
            Assert.That(bolts, Is.GreaterThan(0), "the lances didn't fire at a hostile grid.");
        });

        await pair.CleanReturnAsync();
    }
}
