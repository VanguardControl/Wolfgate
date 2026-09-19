#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Medical.Tourniquet;
using Content.Shared._Onyx.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.EntityEffects;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Alert;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W5: what an untreated wound turns into, and what stops it. Infection accumulates on the wound, spreads
/// to the body as fever and toxins, and ends as sepsis; a dressing all but prevents it, antiseptic drains
/// a local one and an antibiotic clears it at any stage. Necrosis is the other half: a tourniquet nobody
/// took off kills the limb under it, and dead tissue keeps the patient septic until it is amputated.
/// </summary>
/// <remarks>
/// Both systems batch on a five-second timer and advance by whatever time has accumulated, so the tests
/// hand <c>Update</c> the minutes directly rather than waiting for them. Every threshold is data
/// (<c>_WF/Wolfmed/Wounds/infection.yml</c>), so the assertions compare stages and directions, not the
/// numbers themselves.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedInfectionSystem))]
public sealed class WolfmedInfectionTest : GameTest
{
    /// <summary>An untreated cut works its way through local, spreading and septic in that order.</summary>
    [Test]
    public async Task InfectionClimbsThroughItsStagesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var profile = infection.Profile;

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 20);

            var wound = FindWound(entities, torso, "SlashWound");
            Assert.That(entities.HasComponent<WolfmedInfectionComponent>(wound), Is.True,
                "an open cut is contaminated from the moment it exists.");
            Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.None),
                "and does nothing at all until it has had time.");

            // Just past the local threshold at 25 progress and 6 a minute.
            infection.Update(Minutes(5));
            Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.Local));

            var pain = entities.System<PainSystem>().GetPain(torso);
            var severity = entities.GetComponent<WoundComponent>(wound).Severity;
            infection.Update(Minutes(2));
            Assert.Multiple(() =>
            {
                Assert.That(entities.System<PainSystem>().GetPain(torso), Is.GreaterThan(pain),
                    "a locally infected wound hurts more as it goes.");
                Assert.That(entities.GetComponent<WoundComponent>(wound).Severity, Is.GreaterThan(severity),
                    "and reopens faster than it closes, which is what 'slower healing' means here.");
            });

            var poison = Damage(entities, body, "Poison");
            infection.Update(Minutes(5));
            Assert.Multiple(() =>
            {
                Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.Spreading));
                Assert.That(Damage(entities, body, "Poison"), Is.GreaterThan(poison),
                    "past the wound it is a systemic poisoning.");
                Assert.That(entities.GetComponent<WolfmedInfectionComponent>(wound).Progress,
                    Is.GreaterThanOrEqualTo(profile.SpreadingAt));
            });

            // The severity the infection put back is capped, so an ignored scratch cannot become a wound
            // bigger than the model ever intended.
            infection.Update(Minutes(60));
            Assert.That(entities.GetComponent<WolfmedInfectionComponent>(wound).SeverityAdded,
                Is.LessThanOrEqualTo(profile.MaxSeverityAdded));
        });
    }

    /// <summary>
    /// The two cheap answers: a dressing on the wound cuts the rate to a fraction, and antiseptic drains
    /// a local infection back out. Neither touches one that has already spread.
    /// </summary>
    [Test]
    public async Task DressingAndCleaningPreventInfectionTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();

            var dressed = entities.SpawnEntity("MobHuman", map.GridCoords);
            var ignored = entities.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var patient in new[] { dressed, ignored })
                Damage(entities, patient, TargetBodyPart.Torso, "Slash", 20);

            var dressedWound = FindWound(entities, Part(entities, dressed, BodyPartType.Torso), "SlashWound");
            var ignoredWound = FindWound(entities, Part(entities, ignored, BodyPartType.Torso), "SlashWound");
            Assert.That(bleeding.SetTreatment(dressedWound, BleedingTreatment.Bandaged), Is.True);

            infection.Update(Minutes(6));
            Assert.Multiple(() =>
            {
                Assert.That(infection.GetStage(ignoredWound), Is.EqualTo(WolfmedInfectionStage.Local));
                Assert.That(infection.GetStage(dressedWound), Is.EqualTo(WolfmedInfectionStage.None));
                Assert.That(Progress(entities, dressedWound), Is.LessThan(Progress(entities, ignoredWound) / 4f),
                    "gauze early is most of the prevention in the model.");
            });

            // Antiseptic on a local infection: it drains away over the next minute instead of climbing.
            Assert.That(infection.Clean(ignored), Is.EqualTo(1));
            var before = Progress(entities, ignoredWound);
            infection.Update(Minutes(1));
            Assert.That(Progress(entities, ignoredWound), Is.LessThan(before));

            // Once it has spread, cleaning the skin is far too late.
            var septic = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, septic, TargetBodyPart.Torso, "Slash", 20);
            var septicWound = FindWound(entities, Part(entities, septic, BodyPartType.Torso), "SlashWound");

            infection.Update(Minutes(12));
            Assert.That(infection.GetStage(septicWound), Is.EqualTo(WolfmedInfectionStage.Spreading));

            Assert.That(infection.Clean(septic), Is.EqualTo(1));
            var spread = Progress(entities, septicWound);
            infection.Update(Minutes(2));
            Assert.That(Progress(entities, septicWound), Is.GreaterThan(spread),
                "the antiseptic never reaches what has already left the wound.");
        });
    }

    /// <summary>
    /// The two reagent seams. Both entity effects live in Shared and the model does not, so they reach it
    /// by event; this drives the events themselves and checks the humanoid base actually carries the
    /// antiseptic touch reaction that raises the first one.
    /// </summary>
    [Test]
    public async Task ReagentSeamsReachTheModelTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 20);
            var wound = FindWound(entities, Part(entities, body, BodyPartType.Torso), "SlashWound");
            infection.Update(Minutes(6));

            var cleaned = new WolfmedCleanWoundsEvent();
            entities.EventBus.RaiseLocalEvent(body, ref cleaned);
            Assert.Multiple(() =>
            {
                Assert.That(cleaned.Cleaned, Is.EqualTo(1));
                Assert.That(entities.GetComponent<WolfmedInfectionComponent>(wound).Cleaned, Is.True);
            });

            var antibiotic = new WolfmedAntibioticEvent(20f);
            entities.EventBus.RaiseLocalEvent(body, ref antibiotic);
            Assert.Multiple(() =>
            {
                Assert.That(antibiotic.Treated, Is.True);
                Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.None));
            });

            // The wiring that fires the first of those in play.
            var antiseptics = entities.GetComponent<ReactiveComponent>(body).Reactions!
                .Where(entry => entry.Effects.Any(effect => effect is WolfmedCleanWounds))
                .SelectMany(entry => entry.Reagents!)
                .ToList();
            Assert.That(antiseptics, Does.Contain("Ethanol").And.Contain("Spaceacillin"));
        });
    }

    /// <summary>A knife dug around in a wound makes everything that follows worse.</summary>
    [Test]
    public async Task DirtyTreatmentMultipliesContaminationTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();

            var clean = entities.SpawnEntity("MobHuman", map.GridCoords);
            var dirty = entities.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var patient in new[] { clean, dirty })
                Damage(entities, patient, TargetBodyPart.Torso, "Slash", 20);

            var cleanWound = FindWound(entities, Part(entities, clean, BodyPartType.Torso), "SlashWound");
            var dirtyWound = FindWound(entities, Part(entities, dirty, BodyPartType.Torso), "SlashWound");

            // What W1's improvised embedded-object removal calls.
            infection.Contaminate(dirtyWound);
            Assert.That(entities.GetComponent<WolfmedInfectionComponent>(dirtyWound).Contamination,
                Is.EqualTo(infection.Profile.ContaminationMultiplier));

            infection.Update(Minutes(4));
            Assert.That(Progress(entities, dirtyWound), Is.GreaterThan(Progress(entities, cleanWound)));
        });
    }

    /// <summary>
    /// Sepsis: the body-level stage. It shows on the patient's own alerts, poisons them faster the further
    /// it has got, and an antibiotic is the only thing that reverses it.
    /// </summary>
    [Test]
    public async Task SepsisPoisonsAndAntibioticsClearItTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var alerts = entities.System<AlertsSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso);
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 20);
            var wound = FindWound(entities, torso, "SlashWound");

            // Seventeen minutes of nobody doing anything is what the profile costs.
            infection.Update(Minutes(20));
            Assert.Multiple(() =>
            {
                Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.Septic));
                Assert.That(entities.HasComponent<WolfmedSepsisComponent>(body), Is.True);
            });

            var poison = Damage(entities, body, "Poison");
            infection.Update(Minutes(4));
            Assert.Multiple(() =>
            {
                Assert.That(infection.GetSepsis(body), Is.GreaterThan(0f));
                Assert.That(Damage(entities, body, "Poison"), Is.GreaterThan(poison));
                Assert.That(alerts.IsShowingAlert(body, WolfmedInfectionSystem.SepsisAlert), Is.True);
            });

            // A full course: every wound cleared, then the bloodstream.
            Assert.That(infection.Treat(body, 20f), Is.True);
            Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.None),
                "the antibiotic reaches contamination no dressing could.");

            infection.Treat(body, 40f);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WolfmedSepsisComponent>(body), Is.False);
                Assert.That(alerts.IsShowingAlert(body, WolfmedInfectionSystem.SepsisAlert), Is.False);
            });
        });
    }

    /// <summary>
    /// A tourniquet is a ten-minute solution. Left on, the limb under it dies, and the dead limb is then a
    /// standing source of sepsis; loosening it in time gives the bleeding back instead.
    /// </summary>
    [Test]
    public async Task TourniquetLeftOnKillsTheLimbTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var tourniquet = entities.System<TourniquetSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Damage(entities, body, TargetBodyPart.LeftArm, "Slash", 25);

            Assert.That(tourniquet.Apply(body, arm), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WolfmedTourniquetComponent>(arm), Is.True);
                Assert.That(necrosis.IsAtRisk(arm), Is.False, "the clock starts on the first tick, not on application.");
            });

            necrosis.Update(60f);
            Assert.That(necrosis.IsAtRisk(arm), Is.True, "the analyzer flags the limb from the moment it starts.");

            // Well past the ten minutes the profile allows.
            necrosis.Update((float) infection.Profile.TourniquetOnset.TotalSeconds + 60f);
            Assert.Multiple(() =>
            {
                Assert.That(necrosis.IsNecrotic(arm), Is.True);
                Assert.That(Prototypes(entities, arm), Does.Contain("WolfmedNecrosisWound"));
                Assert.That(entities.HasComponent<WolfmedTourniquetComponent>(arm), Is.False,
                    "there is nothing left for the tourniquet to save.");
            });

            // Dead tissue keeps the patient septic on its own, with no other wound involved.
            infection.Update(Minutes(20));
            Assert.That(entities.HasComponent<WolfmedSepsisComponent>(body), Is.True);
        });
    }

    /// <summary>Taking the tourniquet off in time costs the bleeding back and stops the clock.</summary>
    [Test]
    public async Task LooseningATourniquetStopsTheClockTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();
            var tourniquet = entities.System<TourniquetSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Damage(entities, body, TargetBodyPart.LeftArm, "Slash", 25);

            Assert.That(tourniquet.Apply(body, arm), Is.True);
            Assert.That(bleeding.GetPartRate(arm), Is.Zero);
            necrosis.Update(60f);

            Assert.That(necrosis.Loosen(body, arm, body), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WolfmedTourniquetComponent>(arm), Is.False);
                Assert.That(necrosis.IsAtRisk(arm), Is.False);
                Assert.That(bleeding.GetPartRate(arm), Is.GreaterThan(0f), "the trade the verb exists for.");
            });

            necrosis.Update(3600f);
            Assert.That(necrosis.IsNecrotic(arm), Is.False);
        });
    }

    /// <summary>A limb put back on long after it came off goes back on dead.</summary>
    [Test]
    public async Task LateReattachmentKillsTheLimbTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            // Detach and reattach is the real path (WolfmedBodyPartLifecycleSystem drives both of these);
            // the stamp is back-dated because the grace period is wall-clock minutes.
            necrosis.OnDetached(arm);
            Assert.That(necrosis.OnAttached(arm), Is.False, "a limb put straight back is fine.");

            necrosis.OnDetached(arm);
            entities.GetComponent<WolfmedNecrosisComponent>(arm).DetachedAt =
                timing.CurTime - TimeSpan.FromMinutes(30);

            Assert.That(necrosis.OnAttached(arm), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(necrosis.IsNecrotic(arm), Is.True);
                Assert.That(Prototypes(entities, arm), Does.Contain("WolfmedNecrosisWound"));
            });
        });
    }

    /// <summary>
    /// Disabling infection stops it dead without unwinding what is already there, so an admin can turn it
    /// off mid-round.
    /// </summary>
    [Test]
    public async Task CVarsDisableTheModelTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var infection = entities.System<WolfmedInfectionSystem>();
            var necrosis = entities.System<WolfmedNecrosisSystem>();
            var tourniquet = entities.System<TourniquetSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Damage(entities, body, TargetBodyPart.LeftArm, "Slash", 25);
            var wound = FindWound(entities, arm, "SlashWound");

            try
            {
                config.SetCVar(WolfmedCVars.InfectionEnabled, false);
                config.SetCVar(WolfmedCVars.NecrosisEnabled, false);
                Assert.That(tourniquet.Apply(body, arm), Is.True);

                infection.Update(Minutes(60));
                necrosis.Update(3600f);
                Assert.Multiple(() =>
                {
                    Assert.That(Progress(entities, wound), Is.Zero);
                    Assert.That(necrosis.IsNecrotic(arm), Is.False);
                });

                // The rate multiplier is the other half: it scales the same timers rather than gating them.
                config.SetCVar(WolfmedCVars.InfectionEnabled, true);
                config.SetCVar(WolfmedCVars.InfectionRate, 10f);
                infection.Update(Minutes(1));
                Assert.That(infection.GetStage(wound), Is.Not.EqualTo(WolfmedInfectionStage.None));
            }
            finally
            {
                config.SetCVar(WolfmedCVars.InfectionEnabled, true);
                config.SetCVar(WolfmedCVars.NecrosisEnabled, true);
                config.SetCVar(WolfmedCVars.InfectionRate, 1f);
            }
        });
    }

    /// <summary>The analyzer reports the stage per part and sepsis for the body, and the words all exist.</summary>
    [Test]
    public async Task AnalyzerAndNamesExistTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.System<Content.Server.Medical.HealthAnalyzerSystem>();
            var infection = entities.System<WolfmedInfectionSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 20);
            infection.Update(Minutes(20));

            var diagnostics = analyzer.BuildWoundDiagnostics(body);
            Assert.That(diagnostics, Is.Not.Null);
            Assert.That(diagnostics!.Parts.TryGetValue(TargetBodyPart.Torso, out var torso));

            Assert.Multiple(() =>
            {
                Assert.That(torso.Infection, Is.EqualTo(WolfmedInfectionStage.Septic));
                Assert.That(diagnostics.Sepsis, Is.GreaterThanOrEqualTo(0f));

                Assert.That(prototypes.HasIndex<AlertPrototype>(WolfmedInfectionSystem.SepsisAlert));
                Assert.That(prototypes.HasIndex<ReagentPrototype>("Spaceacillin"));
                Assert.That(prototypes.HasIndex<ReactionPrototype>("Spaceacillin"));
                Assert.That(prototypes.HasIndex<WoundPrototype>("WolfmedNecrosisWound"));
                Assert.That(prototypes.HasIndex<WolfmedInfectionProfilePrototype>(WolfmedInfectionSystem.DefaultProfile));

                Assert.That(locale.HasString("wolfmed-wound-name-necrosis"));
                Assert.That(locale.HasString("wolfmed-wound-stage-necrotic"));
                Assert.That(locale.HasString("wolfmed-wound-cleaned"));
                Assert.That(locale.HasString("wolfmed-necrosis-warning"));
                Assert.That(locale.HasString("wolfmed-necrosis-dead"));
                Assert.That(locale.HasString("wolfmed-tourniquet-loosen-verb"));
                Assert.That(locale.HasString("wolfmed-tourniquet-loosened"));
                Assert.That(locale.HasString("health-analyzer-wound-infection-local"));
                Assert.That(locale.HasString("health-analyzer-wound-infection-spreading"));
                Assert.That(locale.HasString("health-analyzer-wound-infection-septic"));
                Assert.That(locale.HasString("health-analyzer-wound-necrotic-short"));
                Assert.That(locale.HasString("health-analyzer-wound-necrosis-risk-short"));
                Assert.That(locale.HasString("health-analyzer-wound-sepsis"));
                Assert.That(locale.HasString("alerts-wolfmed-sepsis-name"));
                Assert.That(locale.HasString("reagent-name-spaceacillin"));
            });
        });
    }

    /// <summary>Seconds for a number of minutes of model time.</summary>
    private static float Minutes(float minutes) => minutes * 60f;

    private static float Progress(IEntityManager entities, EntityUid wound) =>
        entities.TryGetComponent(wound, out WolfmedInfectionComponent? infection) ? infection.Progress : 0f;

    private static void Damage(IEntityManager entities, EntityUid body, TargetBodyPart target,
        string type, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec(type, amount),
            ignoreResistances: true, origin: null, targetPart: target);
    }

    private static FixedPoint2 Damage(IEntityManager entities, EntityUid uid, string type) =>
        entities.GetComponent<DamageableComponent>(uid).Damage.DamageDict
            .GetValueOrDefault(new ProtoId<DamageTypePrototype>(type));

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type,
        BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static List<string> Prototypes(IEntityManager entities, EntityUid part)
    {
        return entities.System<WoundSystem>().GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Select(wound => wound.Comp.Prototype.Id)
            .ToList();
    }

    private static EntityUid FindWound(IEntityManager entities, EntityUid part, string prototype)
    {
        return entities.System<WoundSystem>().GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .First(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype))
            .Owner;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
