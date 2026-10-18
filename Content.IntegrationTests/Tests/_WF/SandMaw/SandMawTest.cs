#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.SandMaw;
using Content.Shared._WF.Tether;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Item;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.SandMaw;

/// <summary>Merak's sand maw: a tentacle drags you in, slashing it frees you, and a swallowed victim tears their way out.</summary>
[TestFixture]
public sealed class SandMawTest
{
    private static async Task<(EntityUid Maw, EntityUid Victim)> Rig(TestPair pair, float distance)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        EntityUid maw = default, victim = default;
        await server.WaitPost(() =>
        {
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -10; x <= 10; x++)
            for (var y = -10; y <= 10; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            maw = entMan.SpawnEntity("WFMerakSandMaw", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            victim = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f + distance, 0.5f));
        });
        return (maw, victim);
    }

    [Test]
    public async Task DraggedInSwallowedAndBreakingOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var (maw, victim) = await Rig(pair, 4f);
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSandMawComponent>(maw);
            Assert.That(comp.Grabs.ContainsKey(victim), Is.True, "The maw should have lashed a tentacle round the human.");
            Assert.That(entMan.GetComponent<RopeAttachPointComponent>(victim).Ropes, Has.Count.EqualTo(1));
        });
        await pair.RunSeconds(8);

        EntityUid belly = default;
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSandMawComponent>(maw);
            Assert.That(comp.Belly, Is.Not.Null, "The human was never reeled in.");
            belly = comp.Belly!.Value;
            Assert.That(entMan.GetComponent<TransformComponent>(victim).GridUid, Is.EqualTo(belly), "The human should be in the belly.");
            Assert.That(entMan.HasComponent<RopeAttachPointComponent>(victim), Is.False, "The tentacle lets go once it has swallowed.");
            var loot = entMan.EntityQuery<ItemComponent>().Count(item => entMan.GetComponent<TransformComponent>(item.Owner).GridUid == belly);
            Assert.That(loot, Is.GreaterThanOrEqualTo(5), "The belly should hold the loot of the fallen.");

            var wall = entMan.EntityQuery<WFSandMawBellyWallComponent>().First(w => w.Maw == maw).Owner;
            var blunt = server.ProtoMan.Index<DamageTypePrototype>("Blunt");
            server.System<DamageableSystem>().TryChangeDamage(wall, new DamageSpecifier(blunt, 200), true);
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var exit = entMan.EntityQuery<WFSandMawExitComponent>().Single(e => e.Maw == maw).Owner;
            xforms.SetCoordinates(victim, entMan.GetComponent<TransformComponent>(exit).Coordinates);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(victim).GridUid, Is.Not.EqualTo(belly), "Stepping into the tear should climb out.");
            var distance = Vector2.Distance(xforms.GetWorldPosition(victim), xforms.GetWorldPosition(maw));
            Assert.That(distance, Is.GreaterThan(entMan.GetComponent<WFSandMawComponent>(maw).GrabRange), "Out past the tentacle's reach.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SlashingTheTentacleLetsGo()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (maw, victim) = await Rig(pair, 5f);
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            var grip = entMan.GetComponent<WFSandMawComponent>(maw).Grabs[victim].Grip;
            var damage = server.System<DamageableSystem>();
            // Blunt barely marks it; an edge cuts it.
            damage.TryChangeDamage(grip, new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 20));
            Assert.That(entMan.Deleted(grip) || entMan.IsQueuedForDeletion(grip), Is.False, "A punch shouldn't cut the tentacle.");
            damage.TryChangeDamage(grip, new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Slash"), 20));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFSandMawComponent>(maw).Grabs.ContainsKey(victim), Is.False, "Slashing should free the human.");
            Assert.That(entMan.HasComponent<RopeAttachPointComponent>(victim), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
