using System.Linq;
using System.Numerics;
using Content.Server._WF.Xenobiology;
using Content.Shared._WF.Xenobiology;
using Content.Shared._WF.Xenobiology.Potions;
using Content.Shared.Actions;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Xenobiology;

/// <summary>
/// The xenobiology loop: splitting, extract reactions, the slime processor and the console's leashed eye.
/// </summary>
[TestFixture]
public sealed class XenobiologyTest
{
    private const string Slime = "XenobiologySlimeGray";
    private const string Extract = "GraySlimeExtract";

    [Test]
    public async Task SplitMakesFourChildrenAndMaxChanceMutatesToRainbow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var slimes = server.System<SlimeSystem>();

        EntityUid plain = default, maxed = default;
        await server.WaitPost(() =>
        {
            plain = entities.SpawnEntity(Slime, map.GridCoords);
            maxed = entities.SpawnEntity(Slime, map.GridCoords.Offset(new Vector2(1, 0)));
            var maxedSlime = entities.GetComponent<SlimeComponent>(maxed);
            maxedSlime.MutationChance = 1;
            entities.GetComponent<SlimeComponent>(plain).MutationChance = 0;

            Assert.That(slimes.TrySplit(plain, 4), Is.True);
            Assert.That(slimes.TrySplit(maxed, 4), Is.True);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var children = entities.EntityQuery<SlimeComponent>(true)
                .Select(s => entities.GetComponent<MetaDataComponent>(s.Owner).EntityPrototype?.ID)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(entities.Deleted(plain) || entities.IsQueuedForDeletion(plain), Is.True);
                Assert.That(children.Count(id => id == Slime), Is.EqualTo(4), "A 0% slime splits into four of itself.");
                Assert.That(children.Count(id => id == "XenobiologySlimeRainbow"), Is.EqualTo(4),
                    "A 100% slime splits into four rainbow slimes.");
            });

            foreach (var slime in entities.EntityQuery<SlimeComponent>(true).ToList())
            {
                entities.DeleteEntity(slime.Owner);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BloodInAGrayExtractMakesMonkeyCubes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var solutions = server.System<SharedSolutionContainerSystem>();

        EntityUid extract = default;
        await server.WaitPost(() =>
        {
            extract = entities.SpawnEntity(Extract, map.GridCoords);
            Assert.That(solutions.TryGetSolution(extract, "container", out var solution), Is.True);
            solutions.TryAddReagent(solution!.Value, "Blood", FixedPoint2.New(5));
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var cubes = entities.EntityQuery<MetaDataComponent>()
                .Count(m => m.EntityPrototype?.ID == "MonkeyCube");
            Assert.Multiple(() =>
            {
                Assert.That(cubes, Is.EqualTo(3), "The gray blood reaction makes three monkey cubes.");
                Assert.That(entities.Deleted(extract) || entities.IsQueuedForDeletion(extract), Is.True,
                    "The reaction uses up and deletes the extract.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProcessorTurnsADeadSlimeIntoExtracts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        EntityUid processor = default, slime = default;
        await server.WaitPost(() =>
        {
            processor = entities.SpawnEntity("SlimeProcessor", map.GridCoords);
            entities.System<SharedPowerReceiverSystem>().SetNeedsPower(processor, false);
            slime = entities.SpawnEntity(Slime, map.GridCoords.Offset(new Vector2(0.5f, 0)));
            entities.GetComponent<SlimeComponent>(slime).SlimeSteroidAmount = 1;
            var cold = new DamageSpecifier(protos.Index<DamageTypePrototype>("Cold"), FixedPoint2.New(250));
            entities.System<DamageableSystem>().TryChangeDamage(slime, cold, true);
        });

        await server.WaitRunTicks(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<SharedContainerSystem>().TryGetContainer(processor,
                SlimeProcessorComponent.SlimeContainerName, out var container), Is.True);
            Assert.That(container!.ContainedEntities, Does.Contain(slime), "The processor pulls in a dead slime.");
            entities.System<SlimeProcessorSystem>().StartProcessing((processor, entities.GetComponent<SlimeProcessorComponent>(processor)));
        });

        await server.WaitRunTicks(250);
        await server.WaitAssertion(() =>
        {
            var extracts = entities.EntityQuery<SlimeExtractComponent>().Count();
            Assert.Multiple(() =>
            {
                Assert.That(extracts, Is.EqualTo(5), "Four extracts from the yield plus one from the steroid.");
                Assert.That(entities.Deleted(slime), Is.True);
                Assert.That(entities.HasComponent<CollectingSlimeProcessorComponent>(processor), Is.True,
                    "The processor goes back to collecting.");
            });

            foreach (var e in entities.EntityQuery<SlimeExtractComponent>().ToList())
            {
                entities.DeleteEntity(e.Owner);
            }

            entities.DeleteEntity(processor);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleEyeStaysOnItsLeashAndClosesWhenTheUserLeaves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var xform = server.System<SharedTransformSystem>();
        var consoles = server.System<XenobiologyConsoleSystem>();

        EntityUid console = default, user = default, eye = default;
        await server.WaitPost(() =>
        {
            console = entities.SpawnEntity("XenobiologyConsole", map.GridCoords);
            user = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1, 0)));
            consoles.OpenEye((console, entities.GetComponent<XenobiologyConsoleComponent>(console)), user);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var userComp = entities.GetComponent<XenobiologyConsoleUserComponent>(user);
            eye = userComp.Eye!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(userComp.GrantedActions, Has.Count.EqualTo(8));
                Assert.That(entities.GetComponent<ActionsComponent>(user).Actions, Is.SupersetOf(userComp.GrantedActions));
            });

            xform.SetWorldPosition(eye, xform.GetWorldPosition(console) + new Vector2(50, 0));
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var range = entities.GetComponent<XenobiologyConsoleComponent>(console).EyeRange;
            var distance = (xform.GetWorldPosition(eye) - xform.GetWorldPosition(console)).Length();
            Assert.That(distance, Is.LessThanOrEqualTo(range + 0.01f), "The eye is pulled back onto its leash.");

            xform.SetWorldPosition(user, xform.GetWorldPosition(console) + new Vector2(5, 0));
        });

        await server.WaitRunTicks(3);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<XenobiologyConsoleUserComponent>(user), Is.False,
                    "Walking away from the console closes the eye.");
                Assert.That(entities.Deleted(eye) || entities.IsQueuedForDeletion(eye), Is.True);
                Assert.That(entities.GetComponent<XenobiologyConsoleComponent>(console).User, Is.Null);
            });

            entities.DeleteEntity(user);
            entities.DeleteEntity(console);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MutationPotionClampsAtFullChance()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var potions = server.System<SlimePotionSystem>();

        await server.WaitAssertion(() =>
        {
            var slime = entities.SpawnEntity(Slime, map.GridCoords);
            var comp = entities.GetComponent<SlimeComponent>(slime);
            comp.MutationChance = 0.95;

            Assert.That(potions.TryApplyMutation((slime, comp), potions.PotionAmount("SlimeMutationPotion"), out _), Is.True);
            Assert.That(comp.MutationChance, Is.EqualTo(FixedPoint2.New(1)));
            Assert.That(potions.TryApplyMutation((slime, comp), potions.PotionAmount("SlimeMutationPotion"), out _), Is.False,
                "A full slime refuses another mutation potion.");
            Assert.That(potions.TryApplyMutation((slime, comp), potions.PotionAmount("SlimeStabilizerPotion"), out _), Is.True);
            Assert.That(comp.MutationChance, Is.EqualTo(FixedPoint2.New(0.85)));

            entities.DeleteEntity(slime);
        });

        await pair.CleanReturnAsync();
    }
}
