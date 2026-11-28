using System.Linq;
using Content.Server._WG.Augments;
using Content.Server._WG.Proteans;
using Content.Shared._WG.Proteans;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Emp;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;
using StatusEffectsSystem = Content.Shared.StatusEffectNew.StatusEffectsSystem;

namespace Content.IntegrationTests.Tests._WG.Proteans;

/// <summary>Nanomass on the seed's battery: upkeep, absorbing and its refusals, feeding, slurry, traces and EMP.</summary>
[TestFixture]
[TestOf(typeof(WGNanomassSystem))]
public sealed class ProteanNanomassTest
{
    private const string Protean = "MobWGProtean";
    private static readonly Robust.Shared.Prototypes.ProtoId<DamageTypePrototype> Blunt = "Blunt";

    /// <summary>Upkeep by game time: a full load lasts about 33 minutes gathered and 16 times that as a puddle.</summary>
    [Test]
    public async Task UpkeepTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var nanomass = entities.System<WGNanomassSystem>();
        const int seconds = 60;

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entities.SpawnEntity(Protean, map.GridCoords);
            // The test map has no air; vacuum damage would shed mass as slurry.
            entities.System<SharedGodmodeSystem>().EnableGodmode(mob);
            nanomass.SetMass(mob, 100f);
        });

        var gathered = await Drain(pair, timing, () => nanomass.GetMass(mob), seconds);
        var fullLoad = 100f / gathered;
        Assert.That(fullLoad / 60f, Is.InRange(31f, 35f), $"a full load lasts {fullLoad / 60f:F1} minutes, not about 33");

        await server.WaitPost(() =>
        {
            entities.GetComponent<WGNanomassComponent>(mob).Pooled = true;
            nanomass.SetMass(mob, 100f);
        });

        var pooled = await Drain(pair, timing, () => nanomass.GetMass(mob), seconds);
        Assert.That(gathered / pooled, Is.InRange(14f, 18f), "a puddle does not last about 16 times longer");

        await pair.CleanReturnAsync();
    }

    /// <summary>Mass lost per second of game time over about the given number of seconds.</summary>
    private static async Task<float> Drain(Pair.TestPair pair, IGameTiming timing, Func<float> mass, int seconds)
    {
        var startMass = 0f;
        var startTime = TimeSpan.Zero;
        await pair.Server.WaitPost(() =>
        {
            startMass = mass();
            startTime = timing.CurTime;
        });

        await pair.RunTicksSync((int) (seconds * timing.TickRate));

        var drained = 0f;
        await pair.Server.WaitPost(() =>
        {
            drained = (startMass - mass()) / (float) (timing.CurTime - startTime).TotalSeconds;
        });
        return drained;
    }

    [Test]
    public async Task AbsorbTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var nanomass = entities.System<WGNanomassSystem>();
        var stacks = entities.System<SharedStackSystem>();

        await server.WaitAssertion(() =>
        {
            var mob = entities.SpawnEntity(Protean, map.GridCoords);
            var ent = (mob, entities.GetComponent<WGNanomassComponent>(mob));
            nanomass.SetMass(mob, 0f);

            // Ten steel sheets fill an empty body; from a stack of thirty, twenty are left.
            var steel = entities.SpawnEntity("SheetSteel", map.GridCoords);
            stacks.SetCount(steel, 30);
            Assert.That(nanomass.TryAbsorb(ent, steel, null), Is.True);
            Assert.That(nanomass.GetMass(mob), Is.EqualTo(100f).Within(0.01f), "ten steel sheets don't fill an empty body");
            Assert.That(stacks.GetCount(steel), Is.EqualTo(20), "the wrong number of sheets was taken");

            var more = entities.SpawnEntity("SheetSteel", map.GridCoords);
            Assert.That(nanomass.TryAbsorb(ent, more, null), Is.False, "a full Protean absorbed more");

            // Each refusal leaves the item whole.
            nanomass.SetMass(mob, 0f);
            var mouse = entities.SpawnEntity("MobMouse", map.GridCoords);
            var card = entities.SpawnEntity("PassengerIDCard", map.GridCoords);
            var paid = entities.SpawnEntity("SheetSteel", map.GridCoords);
            entities.AddComponent<WGAugmentPaidComponent>(paid);
            var box = entities.SpawnEntity("ToolboxMechanical", map.GridCoords);
            var inside = entities.SpawnEntity("SheetSteel", map.GridCoords);
            Assert.That(entities.System<SharedStorageSystem>().Insert(box, inside, out _, playSound: false), Is.True,
                "the toolbox would not take a sheet");

            foreach (var refused in new[] { mouse, card, paid, box })
            {
                Assert.That(nanomass.TryAbsorb(ent, refused, null), Is.False, $"{entities.ToPrettyString(refused)} was absorbed");
                Assert.That(entities.Deleted(refused) || entities.IsQueuedForDeletion(refused), Is.False,
                    $"{entities.ToPrettyString(refused)} was eaten after a refusal");
            }

            Assert.That(nanomass.GetMass(mob), Is.Zero, "a refusal gave mass");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FeedSlurryTraceEmpTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var proto = server.ProtoMan;
        var nanomass = entities.System<WGNanomassSystem>();
        var damageable = entities.System<DamageableSystem>();
        var status = entities.System<StatusEffectsSystem>();
        var blunt = proto.Index(Blunt);

        await server.WaitAssertion(() =>
        {
            var mob = entities.SpawnEntity(Protean, map.GridCoords);
            var ent = (mob, entities.GetComponent<WGNanomassComponent>(mob));
            var feeder = entities.SpawnEntity("MobHuman", map.GridCoords);
            nanomass.SetMass(mob, 100f);

            // Damage sheds 0.3 mass a point as slurry, and walking it back returns it.
            damageable.TryChangeDamage(mob, new DamageSpecifier(blunt, 10));
            Assert.That(nanomass.GetMass(mob), Is.EqualTo(100f - 12f * 0.3f).Within(0.01f), "the slurry took the wrong mass");
            var query = entities.EntityQueryEnumerator<WGProteanSlurryComponent>();
            Assert.That(query.MoveNext(out var slurryUid, out var slurry), Is.True, "no slurry was spilled");
            Assert.That(slurry!.Mass, Is.EqualTo(12f * 0.3f).Within(0.01f));
            Assert.That(nanomass.Reclaim((slurryUid, slurry), mob), Is.True);
            Assert.That(nanomass.GetMass(mob), Is.EqualTo(100f).Within(0.01f), "the slurry didn't come back");
            Assert.That(entities.IsQueuedForDeletion(slurryUid), Is.True, "emptied slurry stayed");

            // Fed by someone else, the Protean heals at once.
            nanomass.SetMass(mob, 50f);
            var before = entities.GetComponent<DamageableComponent>(mob).TotalDamage;
            var sheet = entities.SpawnEntity("SheetSteel", map.GridCoords);
            Assert.That(nanomass.TryAbsorb(ent, sheet, feeder), Is.True);
            Assert.That(entities.GetComponent<DamageableComponent>(mob).TotalDamage, Is.LessThan(before), "feeding didn't heal");

            // Plasteel leaves Hardened, which eases the fragility. (The full steel stack above filled the body.)
            nanomass.SetMass(mob, 50f);
            var plasteel = entities.SpawnEntity("SheetPlasteel", map.GridCoords);
            Assert.That(nanomass.TryAbsorb(ent, plasteel, null), Is.True);
            Assert.That(status.HasStatusEffect(mob, "WGStatusEffectProteanHardened"), Is.True, "plasteel left no trace");
            var hurt = entities.GetComponent<DamageableComponent>(mob).TotalDamage;
            damageable.TryChangeDamage(mob, new DamageSpecifier(blunt, 10));
            Assert.That(entities.GetComponent<DamageableComponent>(mob).TotalDamage - hurt,
                Is.EqualTo(FixedPoint2.New(12 * 0.8)), "Hardened didn't ease the damage");

            // An EMP burns mass from the body but never drains the seed itself.
            var seed = nanomass.GetSeed(mob)!.Value;
            nanomass.SetMass(mob, 50f);
            var emp = entities.System<SharedEmpSystem>();
            Assert.That(emp.TryEmpEffects(seed, 2_700_000f, TimeSpan.FromSeconds(1)), Is.False, "the seed took an EMP");
            Assert.That(nanomass.GetMass(mob), Is.EqualTo(50f).Within(0.01f));
            emp.TryEmpEffects(mob, 2_700_000f, TimeSpan.FromSeconds(1));
            Assert.That(nanomass.GetMass(mob), Is.EqualTo(40f).Within(0.01f), "a heavy EMP didn't burn 10 mass");
        });

        await pair.CleanReturnAsync();
    }
}
