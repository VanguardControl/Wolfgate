#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Medical.Tourniquet;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// V124: the feedback layer. A wound that lands makes a noise keyed by what it is and what it is in, a
/// hit throws blood mist or sparks, and a limb coming off does both plus a puddle.
/// </summary>
/// <remarks>
/// Audio cannot be heard from a test, so what is asserted is the selection: which collection the profile
/// picks, which effect prototype a severity earns, and whether the throttle and the damage gate let it
/// through at all. The five interaction sounds are asserted as data on their own components.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedWoundSfxSystem))]
public sealed class WolfmedWoundSfxTest : GameTest
{
    /// <summary>
    /// The profile answers each wound with the collection its cause and its tissue call for, and every
    /// collection and effect it names exists.
    /// </summary>
    [Test]
    public async Task ProfileSelectsSoundByWoundAndTissueTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var profile = prototypes.Index<WolfmedSfxProfilePrototype>(WolfmedWoundSfxSystem.DefaultProfile);

            Assert.Multiple(() =>
            {
                // Bone first: a fracture is a Blunt wound and must not answer with the bruise sound.
                Assert.That(Collection(profile, prototypes, "BoneFractureWound", organic: true, 40),
                    Is.EqualTo("WolfmedWoundBone"));
                Assert.That(Collection(profile, prototypes, "WolfmedDislocationWound", organic: true, 40),
                    Is.EqualTo("WolfmedWoundBone"));
                Assert.That(Collection(profile, prototypes, "CyberneticFrameFractureWound", organic: false, 40),
                    Is.EqualTo("WolfmedWoundFrame"));

                // Flesh, by cause.
                Assert.That(Collection(profile, prototypes, "SlashWound", organic: true, 30),
                    Is.EqualTo("WolfmedWoundFlesh"));
                Assert.That(Collection(profile, prototypes, "WolfmedGunshotWound", organic: true, 30),
                    Is.EqualTo("WolfmedWoundPierce"));
                Assert.That(Collection(profile, prototypes, "BurnWound", organic: true, 30),
                    Is.EqualTo("WolfmedWoundBurn"));
                Assert.That(Collection(profile, prototypes, "BluntWound", organic: true, 30),
                    Is.EqualTo("WolfmedWoundBlunt"));

                // The same wound ids never reach a chassis, but the tissue filter is what proves the
                // selector is reading the part and not the wound: a chassis answers with metal.
                Assert.That(Collection(profile, prototypes, "WolfmedDentWound", organic: false, 30),
                    Is.EqualTo("WolfmedWoundChassis"));
                Assert.That(Collection(profile, prototypes, "WolfmedShortCircuitWound", organic: false, 30),
                    Is.EqualTo("WolfmedWoundShock"));
                Assert.That(Collection(profile, prototypes, "WolfmedBreachWound", organic: false, 30),
                    Is.EqualTo("WolfmedWoundChassis"));

                // A bruise below the entry's own floor says nothing at all.
                Assert.That(profile.GetWoundSound("BluntWound", prototypes.Index<WoundPrototype>("BluntWound"),
                    true, FixedPoint2.New(6)), Is.Null, "a light bruise is not worth a sound.");
            });

            Assert.Multiple(() =>
            {
                foreach (var entry in profile.WoundSounds)
                {
                    Assert.That(entry.Sound, Is.InstanceOf<SoundCollectionSpecifier>(),
                        "every wound sound is a collection, so it can be swapped in one file.");
                    Assert.That(prototypes.HasIndex<SoundCollectionPrototype>(Id(entry.Sound)), Is.True,
                        $"{Id(entry.Sound)} has no collection.");
                }

                foreach (var tier in profile.OrganicDebris.Concat(profile.MechanicalDebris))
                    Assert.That(prototypes.HasIndex<EntityPrototype>(tier.Effect), Is.True,
                        $"{tier.Effect} has no prototype.");

                foreach (var spec in new[] { profile.OrganicDismemberment, profile.MechanicalDismemberment })
                {
                    Assert.That(spec.Sound, Is.Not.Null);
                    Assert.That(prototypes.HasIndex<SoundCollectionPrototype>(Id(spec.Sound!)), Is.True);
                    Assert.That(spec.Effect, Is.Not.Null);
                    Assert.That(prototypes.HasIndex<EntityPrototype>(spec.Effect!.Value), Is.True);
                }
            });
        });
    }

    /// <summary>Debris comes in three coarse sizes per tissue, and nothing at all below the smallest.</summary>
    [Test]
    public async Task DebrisTiersScaleWithSeverityTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var profile = prototypes.Index<WolfmedSfxProfilePrototype>(WolfmedWoundSfxSystem.DefaultProfile);

            Assert.Multiple(() =>
            {
                Assert.That(profile.GetDebris(true, FixedPoint2.New(1)), Is.Null, "a scratch throws nothing.");
                Assert.That(profile.GetDebris(true, FixedPoint2.New(5))?.Id, Is.EqualTo("WolfmedBloodMistSmall"));
                Assert.That(profile.GetDebris(true, FixedPoint2.New(15))?.Id, Is.EqualTo("WolfmedBloodMistMedium"));
                Assert.That(profile.GetDebris(true, FixedPoint2.New(60))?.Id, Is.EqualTo("WolfmedBloodMistLarge"));

                Assert.That(profile.GetDebris(false, FixedPoint2.New(1)), Is.Null);
                Assert.That(profile.GetDebris(false, FixedPoint2.New(5))?.Id, Is.EqualTo("WolfmedSparkBurstSmall"));
                Assert.That(profile.GetDebris(false, FixedPoint2.New(60))?.Id, Is.EqualTo("WolfmedSparkBurstLarge"));

                // Every debris entity is cheap: a sprite and a clock, nothing that ticks.
                foreach (var tier in profile.OrganicDebris.Concat(profile.MechanicalDebris))
                {
                    var entity = prototypes.Index<EntityPrototype>(tier.Effect);
                    Assert.That(entity.Components.ContainsKey("TimedDespawn"), Is.True,
                        $"{tier.Effect} would never be cleaned up.");
                }
            });
        });
    }

    /// <summary>
    /// A hit that wounds throws debris at the body and is then throttled: nine pellets are one spray, not
    /// nine. The sound shares the same gate.
    /// </summary>
    [Test]
    public async Task HitDebrisSpawnsOnceAndDespawnsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid mist = default;

        await server.WaitAssertion(() =>
        {
            var sfx = entities.System<WolfmedWoundSfxSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            Damage(entities, body, TargetBodyPart.Torso, "Slash", 30);
            var spawned = Debris(entities, map);
            Assert.That(spawned, Has.Count.EqualTo(1), "the hit throws exactly one spray.");
            mist = spawned[0];

            // A second hit in the same breath adds nothing: the body is inside its debris interval.
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 30);
            Assert.That(Debris(entities, map), Has.Count.EqualTo(1), "buckshot is one spray, not nine.");

            var state = entities.GetComponent<WolfmedWoundSfxComponent>(body);
            var profile = sfx.Profile!;
            Assert.Multiple(() =>
            {
                Assert.That(state.NextDebris, Is.GreaterThan(sfx.Now));
                Assert.That(state.NextSound, Is.GreaterThan(sfx.Now));
                Assert.That(sfx.TrySpawnDebris(body, state, profile, true, FixedPoint2.New(50)), Is.Null,
                    "and the throttle refuses a third.");
                Assert.That(sfx.TryPlayWound(body, state, profile, "SlashWound", null, true, FixedPoint2.New(50)),
                    Is.False);
            });
        });

        // Half a second of ticks is past every tier's lifetime.
        await Pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
            Assert.That(entities.Deleted(mist), Is.True, "debris cleans itself up."));
    }

    /// <summary>
    /// The damage gate. A wound that appears without a hit behind it - an infection landing, a necrosis
    /// clock running out - is silent and throws nothing, which is the whole reason the gate exists.
    /// </summary>
    [Test]
    public async Task TimeBasedWoundsAreSilentTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);

            // Created straight on the part, exactly as the infection and necrosis timers create theirs.
            Assert.That(wounds.CreateOrMergeWound(torso, "SlashWound", FixedPoint2.New(40)), Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(Debris(entities, map), Is.Empty, "a wound nothing hit throws no debris.");
                Assert.That(entities.HasComponent<WolfmedWoundSfxComponent>(body), Is.False,
                    "and never even stamps the body.");
            });

            // The same wound worsening under a hit does both.
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 30);
            Assert.That(Debris(entities, map), Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// A limb torn off sounds different, and leaves the body's own fluid on the deck: blood for flesh,
    /// oil for a chassis, because the reagent is read off the bloodstream rather than named in the profile.
    /// </summary>
    [Test]
    public async Task DismembermentSoundsAndSpillsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ProtoMan;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var amputation = entities.System<AmputationSystem>();
            var dismemberment = entities.System<WolfmedDismembermentSystem>();
            var profile = prototypes.Index<WolfmedSfxProfilePrototype>(WolfmedWoundSfxSystem.DefaultProfile);

            var flesh = entities.SpawnEntity("MobHuman", map.GridCoords);
            var machine = entities.SpawnEntity("MobIPC", map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(Id(profile.OrganicDismemberment.Sound!), Is.EqualTo("WolfmedDismemberOrganic"),
                    "flesh tears.");
                Assert.That(Id(profile.MechanicalDismemberment.Sound!), Is.EqualTo("WolfmedDismemberMechanical"),
                    "a chassis shears.");
                Assert.That(profile.MechanicalDismemberment.Effect?.Id, Does.StartWith("WolfmedSparkBurst"));
            });

            // The spill is the body's own reagent. Asserted through the public helper so the puddle is
            // attributable to this call and not to any bleeding that ran alongside it.
            var bloodPuddle = dismemberment.TrySpill(flesh, profile.OrganicDismemberment.SpillVolume);
            var oilPuddle = dismemberment.TrySpill(machine, profile.MechanicalDismemberment.SpillVolume);

            Assert.Multiple(() =>
            {
                Assert.That(bloodPuddle, Is.Not.Null);
                Assert.That(entities.HasComponent<PuddleComponent>(bloodPuddle!.Value), Is.True);
                Assert.That(oilPuddle, Is.Not.Null);
                Assert.That(entities.HasComponent<PuddleComponent>(oilPuddle!.Value), Is.True);
            });

            // And the real path: an amputation from damage raises the event the system listens to.
            var arm = entities.System<SharedBodySystem>().GetBodyChildren(flesh)
                .First(part => part.Component.PartType == BodyPartType.Arm).Id;
            Assert.That(amputation.TryAmputate(flesh, arm), Is.True);
            Assert.That(entities.GetComponent<WolfmedWoundSfxComponent>(flesh).NextSound,
                Is.GreaterThan(entities.System<WolfmedWoundSfxSystem>().Now),
                "the tear stamps the shared throttle, so the stump's wounds do not double it.");
        });
    }

    /// <summary>
    /// The five interactions phase 8 left silent all have a sound, and every one of them names a shipped
    /// collection. Data only: the systems play whatever these fields hold.
    /// </summary>
    [Test]
    public async Task InteractionSoundsAreDeclaredTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ProtoMan;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cautery = prototypes.Index<WolfmedCauteryProfilePrototype>(WolfmedCauterySystem.DefaultProfile);
            var dislocation = prototypes.Index<WoundPrototype>("WolfmedDislocationWound");
            Assert.That(dislocation.TryGetBehavior(FixedPoint2.New(40), out WolfmedDislocationBehavior relocate),
                Is.True);

            var embedded = new WolfmedEmbeddedObjectComponent();
            var tourniquet = new WolfmedTourniquetComponent();
            var wrench = entities.SpawnEntity("Wrench", map.GridCoords);

            var sounds = new (string What, SoundSpecifier? Sound)[]
            {
                ("embedded removal start", embedded.BeginSound),
                ("embedded removal finish", embedded.EndSound),
                ("relocate start", relocate.BeginSound),
                ("relocate finish", relocate.EndSound),
                ("deliberate cautery start", cautery.DeliberateBeginSound),
                ("deliberate cautery finish", cautery.DeliberateEndSound),
                ("tourniquet loosen", tourniquet.LoosenSound),
                ("wrench repair finish",
                    entities.GetComponent<WolfmedRepairSoundComponent>(wrench).EndSound),
            };

            Assert.Multiple(() =>
            {
                foreach (var (what, sound) in sounds)
                {
                    Assert.That(sound, Is.Not.Null, $"{what} has no sound.");
                    Assert.That(prototypes.HasIndex<SoundCollectionPrototype>(Id(sound!)), Is.True,
                        $"{what} names a collection that does not exist.");
                }

                // The tourniquet item's own two sounds are Onyx's and were already set; this is the pair
                // taking it off, which had nothing.
                Assert.That(prototypes.Index<EntityPrototype>("Tourniquet")
                    .Components.ContainsKey("Tourniquet"), Is.True);
            });
        });
    }

    /// <summary>Both CVars exist, default on, and silence their half of the layer when turned off.</summary>
    [Test]
    public async Task CVarsDisableTheLayerTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        Assert.Multiple(() =>
        {
            Assert.That(server.CfgMan.GetCVar(WolfmedCVars.WoundSfx), Is.True);
            Assert.That(server.CfgMan.GetCVar(WolfmedCVars.HitDebris), Is.True);
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WolfmedCVars.HitDebris.Name, false);
            server.CfgMan.SetCVar(WolfmedCVars.WoundSfx.Name, false);
        });

        await server.WaitAssertion(() =>
        {
            var sfx = entities.System<WolfmedWoundSfxSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            Damage(entities, body, TargetBodyPart.Torso, "Slash", 40);

            var state = entities.GetComponent<WolfmedWoundSfxComponent>(body);
            Assert.Multiple(() =>
            {
                Assert.That(Debris(entities, map), Is.Empty, "wolfmed.hit_debris off spawns nothing.");
                Assert.That(state.NextSound, Is.EqualTo(TimeSpan.Zero),
                    "wolfmed.wound_sfx off never even opens the throttle.");
                Assert.That(sfx.TryPlayWound(body, state, sfx.Profile!, "SlashWound", null, true,
                    FixedPoint2.New(50)), Is.False);
            });
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WolfmedCVars.HitDebris.Name, true);
            server.CfgMan.SetCVar(WolfmedCVars.WoundSfx.Name, true);
        });
    }

    private static string Collection(
        WolfmedSfxProfilePrototype profile,
        IPrototypeManager prototypes,
        string wound,
        bool organic,
        int severity)
    {
        var sound = profile.GetWoundSound(wound, prototypes.Index<WoundPrototype>(wound), organic,
            FixedPoint2.New(severity));
        Assert.That(sound, Is.Not.Null, $"{wound} picked no sound.");
        return Id(sound!);
    }

    private static string Id(SoundSpecifier sound) =>
        sound is SoundCollectionSpecifier collection ? collection.Collection ?? string.Empty : string.Empty;

    /// <summary>Every Wolfmed debris effect currently alive on the test map.</summary>
    private static List<EntityUid> Debris(IEntityManager entities, TestMapData map)
    {
        var found = new List<EntityUid>();
        var query = entities.EntityQueryEnumerator<TimedDespawnComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityPrototype?.ID.StartsWith("WolfmedBloodMist") == true ||
                meta.EntityPrototype?.ID.StartsWith("WolfmedSparkBurst") == true)
                found.Add(uid);
        }

        return found;
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type) =>
        entities.System<SharedBodySystem>().GetBodyChildren(body)
            .First(part => part.Component.PartType == type).Id;

    private static void Damage(
        IEntityManager entities,
        EntityUid body,
        TargetBodyPart target,
        string type,
        int amount)
    {
        var spec = new DamageSpecifier
        {
            DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
        };
        entities.System<DamageableSystem>().TryChangeDamage(body, spec, origin: null, targetPart: target);
    }
}
