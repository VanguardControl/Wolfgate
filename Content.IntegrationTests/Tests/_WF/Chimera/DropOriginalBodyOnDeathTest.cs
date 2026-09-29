#nullable enable
using System.Collections.Generic;
using Content.Server._WF.Chimera;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Polymorph.Components;
using Content.Server.Polymorph.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Chimera;

/// <summary>
/// A fleshbeast turned from a body drops that body, purged and holding the mind, when it dies or is gibbed.
/// </summary>
[TestFixture]
[TestOf(typeof(DropOriginalBodyOnDeathSystem))]
public sealed class DropOriginalBodyOnDeathTest
{
    private const string BodyProto = "MobHuman";
    private const string MappedBeastProto = "MobLetoferolHorror";
    private const string Letoferol = "Letoferol";
    private const string NaturalLetoferol = "NaturalLetoferol";
    private const string Piercing = "Piercing";

    [TestCase("LetoferolMutationNatural")]
    [TestCase("LetoferolMutation")] // Reverts on death, which the corpse must not do.
    public async Task DeadFleshbeastDropsBody(string polymorph)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var damageSys = entMan.System<DamageableSystem>();
        var mobState = entMan.System<MobStateSystem>();

        var body = EntityUid.Invalid;
        var beast = EntityUid.Invalid;
        var mapped = EntityUid.Invalid;
        var mind = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            (body, beast, mind) = Turn(entMan, map.GridCoords, polymorph);

            // A fleshbeast that was never a body, like the ghost-role and mapped ones, has nothing to drop.
            mapped = entMan.SpawnEntity(MappedBeastProto, map.GridCoords);

            var lethal = new DamageSpecifier(protoMan.Index<DamageTypePrototype>(Piercing), 1000);
            damageSys.TryChangeDamage(beast, lethal, ignoreResistances: true, canSever: false);
            damageSys.TryChangeDamage(mapped, lethal, ignoreResistances: true, canSever: false);
            Assert.That(mobState.IsDead(beast), Is.True, "1000 piercing didn't kill the fleshbeast.");
        });

        // Enough for the polymorph revert check and a metabolism tick that would turn an unpurged body again.
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(beast), Is.False, "The fleshbeast's corpse should stay.");
                Assert.That(entMan.HasComponent<PolymorphedEntityComponent>(beast), Is.False,
                    "The corpse is still a polymorph, so something could revert it into the body.");
                var beastMeta = entMan.GetComponent<MetaDataComponent>(beast);
                Assert.That(beastMeta.EntityName, Is.EqualTo(beastMeta.EntityPrototype!.Name),
                    "The corpse kept the name of the person who fell out of it.");
                AssertDropped(entMan, map.MapUid, body, mind);
                Assert.That(!entMan.Deleted(mapped) && mobState.IsDead(mapped), Is.True,
                    "A fleshbeast that was never turned should just die.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GibbedFleshbeastDropsBody()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var bodySys = entMan.System<BodySystem>();

        var body = EntityUid.Invalid;
        var beast = EntityUid.Invalid;
        var mind = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            (body, beast, mind) = Turn(entMan, map.GridCoords, "LetoferolMutationNatural");

            // Gibbing a living fleshbeast skips its death.
            Assert.That(bodySys.GibBody(beast), Is.Not.Empty, "The fleshbeast didn't gib.");
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(beast), Is.True, "The gibbed fleshbeast should be gone.");
                AssertDropped(entMan, map.MapUid, body, mind);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Spawns a body with a mind and both kinds of Letoferol in its blood and stomach, kills it and turns it.
    /// </summary>
    private static (EntityUid Body, EntityUid Beast, EntityUid Mind) Turn(
        IEntityManager entMan,
        EntityCoordinates coords,
        string polymorph)
    {
        var mindSys = entMan.System<SharedMindSystem>();
        var solutionSys = entMan.System<SharedSolutionContainerSystem>();
        var bodySys = entMan.System<BodySystem>();

        var body = entMan.SpawnEntity(BodyProto, coords);
        var mind = mindSys.CreateMind(null).Owner;
        mindSys.TransferTo(mind, body);

        Assert.That(solutionSys.TryGetSolution(body, BloodstreamComponent.DefaultChemicalsSolutionName, out var chemicals),
            Is.True, $"{BodyProto} has no chemicals solution.");
        var fromBlood = new List<ReagentData> { new DnaData { DNA = "fleshbeast" } };
        solutionSys.TryAddReagent(chemicals!.Value, NaturalLetoferol, 30, data: fromBlood);
        solutionSys.TryAddReagent(chemicals.Value, Letoferol, 20);

        var stomachs = bodySys.GetBodyOrganEntityComps<StomachComponent>(body);
        Assert.That(stomachs, Is.Not.Empty, $"{BodyProto} has no stomach.");
        Assert.That(solutionSys.TryGetSolution(stomachs[0].Owner, StomachSystem.DefaultSolutionName, out var stomach),
            Is.True);
        solutionSys.TryAddReagent(stomach!.Value, Letoferol, 10);
        Assert.That(Letoferols(entMan, body), Is.EqualTo(FixedPoint2.New(60)));

        entMan.System<MobStateSystem>().ChangeMobState(body, MobState.Dead);
        var beast = entMan.System<PolymorphSystem>().PolymorphEntity(body, polymorph);
        Assert.That(beast, Is.Not.Null, $"{polymorph} didn't turn a dead body.");
        Assert.That(entMan.HasComponent<DropOriginalBodyOnDeathComponent>(beast!.Value), Is.True,
            $"{polymorph} turns bodies into a fleshbeast without {nameof(DropOriginalBodyOnDeathComponent)}.");

        return (body, beast.Value, mind);
    }

    /// <summary>
    /// Checks the body is back on the map, holds the mind and carries no Letoferol.
    /// </summary>
    private static void AssertDropped(IEntityManager entMan, EntityUid map, EntityUid body, EntityUid mind)
    {
        Assert.That(entMan.Deleted(body), Is.False, "The original body was deleted.");
        Assert.That(entMan.GetComponent<TransformComponent>(body).MapUid, Is.EqualTo(map),
            "The original body didn't fall out onto the fleshbeast's map.");
        Assert.That(entMan.GetComponent<MetaDataComponent>(body).EntityPaused, Is.False);
        Assert.That(entMan.GetComponent<MindComponent>(mind).OwnedEntity, Is.EqualTo(body),
            "The mind didn't go back to the original body.");
        Assert.That(Letoferols(entMan, body), Is.EqualTo(FixedPoint2.Zero),
            "Letoferol left in the dropped body would turn it again.");
    }

    /// <summary>
    /// Both kinds of Letoferol across the body's own solutions and its organs'.
    /// </summary>
    private static FixedPoint2 Letoferols(IEntityManager entMan, EntityUid body)
    {
        var solutionSys = entMan.System<SharedSolutionContainerSystem>();
        var total = FixedPoint2.Zero;

        var holders = new List<EntityUid> { body };
        foreach (var organ in entMan.System<BodySystem>().GetBodyOrgans(body))
        {
            holders.Add(organ.Id);
        }

        foreach (var holder in holders)
        {
            foreach (var (_, soln) in solutionSys.EnumerateSolutions(holder))
            {
                total += soln.Comp.Solution.GetTotalPrototypeQuantity(Letoferol, NaturalLetoferol);
            }
        }

        return total;
    }
}
