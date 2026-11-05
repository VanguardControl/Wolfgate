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
}
