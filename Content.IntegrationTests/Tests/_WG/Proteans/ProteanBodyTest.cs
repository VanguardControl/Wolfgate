#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.Medical;
using Content.Shared._Goobstation.Autosurgeon;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WG.Augments;
using Content.Shared._WG.Biotype;
using Content.Shared._WG.Proteans;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Nutrition.Components;
using Content.Shared.Power.Components;
using Content.Server.Body.Components;
using Content.Server.Destructible;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Proteans;

/// <summary>The Protean body: what it lacks, how it takes damage, and what it refuses (surgery, augments, hardsuits).</summary>
[TestFixture]
[TestOf(typeof(WGProteanSystem))]
public sealed class ProteanBodyTest
{
    private const string Protean = "MobWGProtean";
    private const string Species = "WGProtean";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> Poison = "Poison";

    [Test]
    public async Task BodyHasNoOrganicSystemsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var bodies = entities.System<SharedBodySystem>();

        await server.WaitAssertion(() =>
        {
            var mob = entities.SpawnEntity(Protean, map.GridCoords);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WoundHostComponent>(mob), Is.False, "a Protean is a wound host");
                Assert.That(entities.HasComponent<RespiratorComponent>(mob), Is.False, "a Protean breathes");
                Assert.That(entities.HasComponent<HungerComponent>(mob), Is.False, "a Protean gets hungry");
                Assert.That(entities.HasComponent<ThirstComponent>(mob), Is.False, "a Protean gets thirsty");
                Assert.That(entities.HasComponent<BloodstreamComponent>(mob), Is.False, "a Protean bleeds");
                Assert.That(entities.HasComponent<SurgeryTargetComponent>(mob), Is.False, "a Protean can be operated on");

                var organs = bodies.GetBodyOrgans(mob).ToList();
                Assert.That(organs, Has.Count.EqualTo(1), "a Protean has organs other than its seed");
                Assert.That(entities.HasComponent<WGProteanSeedComponent>(organs[0].Id), "the one organ is not the seed");
                Assert.That(entities.GetComponent<BatteryComponent>(organs[0].Id).CurrentCharge, Is.EqualTo(100f));

                var parts = bodies.GetBodyChildren(mob).ToList();
                Assert.That(parts, Has.Count.EqualTo(10), "the body lacks the usual part layout");
                Assert.That(parts.Count(p => p.Component.PartType == BodyPartType.Arm), Is.EqualTo(2), "the arms lost their part type");
                foreach (var (part, component) in parts)
                {
                    Assert.That(component.CanSever, Is.False, $"{entities.ToPrettyString(part)} can be severed");
                    if (entities.TryGetComponent(part, out DestructibleComponent? destructible))
                        Assert.That(destructible.Thresholds, Is.Empty, $"{entities.ToPrettyString(part)} can be destroyed");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FlatDamageWithFragilityTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var damageable = entities.System<DamageableSystem>();
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var mob = entities.SpawnEntity(Protean, map.GridCoords);
            var analyzer = entities.SpawnEntity("HandheldHealthAnalyzer", map.GridCoords);
            entities.System<WGProteanSystem>(); // the system must exist

            damageable.TryChangeDamage(mob, new DamageSpecifier(proto.Index(Blunt), 10));
            Assert.That(entities.GetComponent<DamageableComponent>(mob).TotalDamage, Is.EqualTo(FixedPoint2.New(12)),
                "Blunt is not taken flat at 1.2");

            damageable.TryChangeDamage(mob, new DamageSpecifier(proto.Index(Poison), 10));
            Assert.That(entities.GetComponent<DamageableComponent>(mob).TotalDamage, Is.EqualTo(FixedPoint2.New(12)),
                "Poison hurts a Protean");

            Assert.DoesNotThrow(() => entities.System<HealthAnalyzerSystem>().UpdateScannedUser(analyzer, mob, true),
                "the health analyzer throws on a Protean");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BiotypeIsLockedTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var species = proto.Index<SpeciesPrototype>(Species);
            Assert.That(WGBiotypeRules.Resolve(WGBiotype.Organic, species), Is.EqualTo(WGBiotype.Synthetic));

            var mob = entities.SpawnEntity(Protean, map.GridCoords);
            entities.System<SharedWGBiotypeSystem>().Apply(mob, WGBiotype.Organic, species);
            entities.System<SharedWGBiotypeSystem>().Apply(mob, WGBiotype.Synthetic, species);
            Assert.That(entities.GetComponent<BodyComponent>(mob).Prototype?.Id, Is.EqualTo("WGProtean"),
                "a Protean was converted to another biotype's body");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NoAugmentsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var proto = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.That(WGAugmentRules.NoAugmentsReason(Species, proto, factory), Is.Not.Null);
            foreach (var slot in System.Enum.GetValues<WGAugmentSlot>())
            {
                Assert.That(WGAugmentRules.SlotAvailable(Species, slot, proto, factory), Is.False, $"{slot} is offered");
            }

            // A profile saved with picks loads with them dropped, and nothing is due for them.
            var limb = WGAugmentRules.AugmentsFor("Human", WGAugmentSlot.LeftArm, proto, factory)
                .First(a => a.Kind == WGAugmentKind.Limb && a.Price > 0);
            var picks = new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.LeftArm] = new(limb.ID, WGAugmentRules.StylesFor(WGAugmentSlot.LeftArm, proto).First().ID),
            };
            var valid = WGAugmentRules.Validate(Species, picks, [], proto, factory);
            Assert.That(valid, Is.Empty, "a Protean kept an augment");
            var profile = Shared.Preferences.HumanoidCharacterProfile.DefaultWithSpecies(Species).WithAugments(valid);
            Assert.That(WGAugmentRules.Due(profile, proto), Is.Zero, "a Protean is charged for a dropped augment");

            // An autosurgeon does nothing; on a human it starts its work.
            var protean = entities.SpawnEntity(Protean, map.GridCoords);
            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var toggle = entities.System<ItemToggleSystem>();
            var refused = entities.SpawnEntity("AutosurgeonRightSpeedLeg", map.GridCoords);
            var used = entities.SpawnEntity("AutosurgeonRightSpeedLeg", map.GridCoords);
            toggle.TryActivate(refused, protean);
            toggle.TryActivate(used, human);
            Assert.That(entities.System<AutoSurgeonSystem>().WgRefusesBody(protean), Is.True);
            Assert.That(DoAfters(entities, refused), Is.Zero, "the autosurgeon started on a Protean");
            Assert.That(DoAfters(entities, used), Is.GreaterThan(0), "the control autosurgeon never started");
        });

        await pair.CleanReturnAsync();
    }

    private static int DoAfters(IEntityManager entities, EntityUid uid)
    {
        return entities.TryGetComponent(uid, out DoAfterComponent? doAfter) ? doAfter.DoAfters.Count : 0;
    }

    [Test]
    public async Task HardsuitRefusedTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var inventory = entities.System<InventorySystem>();

        await server.WaitAssertion(() =>
        {
            var protean = entities.SpawnEntity(Protean, map.GridCoords);
            var dresser = entities.SpawnEntity("MobHuman", map.GridCoords);
            var suit = entities.SpawnEntity("ClothingOuterHardsuitBasic", map.GridCoords);
            var coords = entities.GetComponent<TransformComponent>(suit).Coordinates;

            Assert.That(inventory.TryEquip(protean, suit, "outerClothing", silent: true), Is.False, "a Protean put on a hardsuit");
            Assert.That(inventory.TryEquip(dresser, protean, suit, "outerClothing", silent: true), Is.False,
                "someone else put a hardsuit on a Protean");
            Assert.That(inventory.TryGetSlotEntity(protean, "outerClothing", out _), Is.False);
            Assert.That(entities.GetComponent<TransformComponent>(suit).Coordinates, Is.EqualTo(coords), "the hardsuit moved");

            // The refusal shows above the Protean, not at the cursor; other refusals keep the cursor.
            Assert.That(inventory.CanEquip(dresser, protean, suit, "outerClothing", out var reason), Is.False);
            Assert.That(inventory.WgEquipRefusalOnMob(reason, dresser, protean), Is.True, "the refusal would show at the cursor");
            Assert.That(inventory.WgEquipRefusalOnMob("inventory-component-can-equip-cannot", dresser, protean), Is.False);

            // The helmet that comes with it is refused as well.
            var helmet = entities.GetComponent<ToggleableClothingComponent>(suit).ClothingUids.Keys.FirstOrDefault();
            if (helmet.IsValid())
                Assert.That(inventory.TryEquip(protean, helmet, "head", silent: true), Is.False, "a Protean put on a hardsuit helmet");

            // Ordinary clothing still goes on, and the dresser can still wear the hardsuit.
            var coat = entities.SpawnEntity("ClothingOuterCoatLab", map.GridCoords);
            Assert.That(inventory.TryEquip(protean, coat, "outerClothing", silent: true), Is.True, "a Protean refused a lab coat");
            Assert.That(inventory.TryEquip(dresser, suit, "outerClothing", silent: true), Is.True, "the refusal reached a human");
        });

        await pair.CleanReturnAsync();
    }
}
