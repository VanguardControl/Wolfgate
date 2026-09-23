#nullable enable
using System.Linq;
using Content.Client._WF.Wolfmed.Overlays;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Hud;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// HUD: the synthetic diagnostics readout. The fault list is built on the server from the wounds and the
/// body conditions and pushed to one client; the escalation tiers and the on-screen layout are pure
/// functions, so both are measured here rather than looked at.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedSyntheticHudSystem))]
public sealed class WolfmedSyntheticHudTest : GameTest
{
    /// <summary>
    /// A breach on the torso and a servo run cut in the left arm read as two lines, tagged by the data,
    /// named by the part, worst of the new ones first. A third fault arriving later goes above both.
    /// </summary>
    [Test]
    public async Task FaultsBuildFromWoundsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hudSystem = entities.System<WolfmedSyntheticHudSystem>();
            var wounds = entities.System<WoundSystem>();

            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            Assert.That(hudSystem.IsMechanicalBody(body), Is.True);

            var torso = Part(entities, body, BodyPartType.Torso);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            wounds.CreateOrMergeWound(torso, "WolfmedBreachWound", FixedPoint2.New(20));
            wounds.CreateOrMergeWound(arm, "WolfmedServoDamageWound", FixedPoint2.New(20));

            var hud = entities.EnsureComponent<WolfmedSyntheticHudComponent>(body);
            hudSystem.Refresh((body, hud));

            Assert.That(hud.Faults, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                // A cut servo is already a [CRIT]; a fresh breach is only a [WARN], so it sorts under it.
                Assert.That(hud.Faults[0].Part, Is.EqualTo(TargetBodyPart.LeftArm));
                Assert.That(hud.Faults[0].Line, Is.EqualTo("wolfmed-synthetic-line-servo"));
                Assert.That(hud.Faults[0].Severity, Is.EqualTo(WolfmedSyntheticSeverity.Crit));
                Assert.That(hud.Faults[1].Part, Is.EqualTo(TargetBodyPart.Torso));
                Assert.That(hud.Faults[1].Line, Is.EqualTo("wolfmed-synthetic-line-breach"));
                Assert.That(hud.Faults[1].Severity, Is.EqualTo(WolfmedSyntheticSeverity.Warn));
                Assert.That(hud.Advice, Is.EqualTo("wolfmed-synthetic-advice-servo"),
                    "the status line advises on the line at the top.");
            });

            // Newest first: a fault the readout did not have last time goes above everything already on it.
            var head = Part(entities, body, BodyPartType.Head);
            wounds.CreateOrMergeWound(head, "WolfmedOverheatingWound", FixedPoint2.New(50));
            hudSystem.Refresh((body, hud));

            Assert.That(hud.Faults, Has.Count.EqualTo(3));
            Assert.Multiple(() =>
            {
                Assert.That(hud.Faults[0].Line, Is.EqualTo("wolfmed-synthetic-line-overheating"));
                Assert.That(hud.Faults[0].Severity, Is.EqualTo(WolfmedSyntheticSeverity.Crit),
                    "past its escalation severity the data gives it the louder tag.");
                Assert.That(hud.Faults[1].Line, Is.EqualTo("wolfmed-synthetic-line-servo"));
                Assert.That(hud.Faults[2].Line, Is.EqualTo("wolfmed-synthetic-line-breach"));
            });

            // A breach on every part it will take is more lines than the wire carries.
            foreach (var (part, _) in entities.System<SharedBodySystem>().GetBodyChildren(body))
            {
                if (wounds.CanCreateWound(part, "WolfmedBreachWound"))
                    wounds.CreateOrMergeWound(part, "WolfmedBreachWound", FixedPoint2.New(20));

                if (wounds.CanCreateWound(part, "WolfmedDentWound"))
                    wounds.CreateOrMergeWound(part, "WolfmedDentWound", FixedPoint2.New(20));
            }

            hudSystem.Refresh((body, hud));
            Assert.That(hud.Faults, Has.Count.EqualTo(WolfmedSyntheticHudComponent.MaxFaults),
                "the readout is capped and never grows past what it can draw.");
        });
    }

    /// <summary>Flesh is not a chassis: no readout component, and none is ever filled for it.</summary>
    [Test]
    public async Task OrganicBodyHasNoReadoutTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hudSystem = entities.System<WolfmedSyntheticHudSystem>();
            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, human, BodyPartType.Torso);
            entities.System<WoundSystem>().CreateOrMergeWound(torso, "WolfmedGunshotWound", FixedPoint2.New(30));

            Assert.Multiple(() =>
            {
                Assert.That(hudSystem.IsMechanicalBody(human), Is.False);
                Assert.That(entities.HasComponent<WolfmedSyntheticHudComponent>(human), Is.False);
            });
        });
    }

    /// <summary>
    /// The escalation bands. Depth and lost integrity are interchangeable inputs and the worse of the two
    /// wins, so a chassis that is barely scratched but nearly unconscious still gets the loud readout.
    /// </summary>
    [Test]
    public void TierBandsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0f, 1f), Is.EqualTo(WolfmedSyntheticTier.Idle));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0f, 0.95f), Is.EqualTo(WolfmedSyntheticTier.Idle));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0f, 0.9f), Is.EqualTo(WolfmedSyntheticTier.Light));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0.2f, 1f), Is.EqualTo(WolfmedSyntheticTier.Light));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0f, 0.7f), Is.EqualTo(WolfmedSyntheticTier.Moderate));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0.4f, 1f), Is.EqualTo(WolfmedSyntheticTier.Moderate));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0f, 0.4f), Is.EqualTo(WolfmedSyntheticTier.Heavy));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0.8f, 1f), Is.EqualTo(WolfmedSyntheticTier.Heavy));
            // The worse of the two, never the sum.
            Assert.That(WolfmedSyntheticHudLineSystem.Strain(0.3f, 0.9f), Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(WolfmedSyntheticHudLineSystem.Strain(0.1f, 0.5f), Is.EqualTo(0.5f).Within(0.001f));
        });
    }

    /// <summary>
    /// The readout is data: every mechanical wound resolves to a line (its own or the fallback row), and
    /// every locale key the table names exists.
    /// </summary>
    [Test]
    public async Task EveryMechanicalWoundHasALineTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var lines = entities.System<WolfmedSyntheticHudLineSystem>();

            Assert.Multiple(() =>
            {
                foreach (var line in prototypes.EnumeratePrototypes<WolfmedSyntheticHudLinePrototype>())
                {
                    Assert.That(locale.HasString(line.Line.Id), Is.True,
                        $"{line.ID} has no readout string ({line.Line.Id}).");
                    Assert.That(line.Advice.Id, Is.Not.Empty, $"{line.ID} names no advice.");
                    Assert.That(locale.HasString(line.Advice.Id), Is.True,
                        $"{line.ID} has no advice string ({line.Advice.Id}).");
                    Assert.That(line.Wound == null || line.Condition == WolfmedSyntheticCondition.None, Is.True,
                        $"{line.ID} is keyed on both a wound and a condition.");
                }

                // Every condition the server can report has a row, or it would be silently dropped.
                foreach (var condition in Enum.GetValues<WolfmedSyntheticCondition>())
                {
                    if (condition == WolfmedSyntheticCondition.None)
                        continue;

                    Assert.That(lines.TryGetConditionLine(condition, out _), Is.True,
                        $"no readout line for condition {condition}.");
                }

                foreach (var wound in prototypes.EnumeratePrototypes<WoundPrototype>()
                             .Where(wound => wound.AnalyzerCategory == WolfmedWoundCategory.Mechanical)
                             .OrderBy(wound => wound.ID))
                {
                    Assert.That(lines.TryGetWoundLine(wound.ID, FixedPoint2.New(50), true, out _, out _), Is.True,
                        $"{wound.ID} resolves to no readout line, not even the fallback.");
                }
            });

            // The client renders the severity tag and the part name by key, so those must resolve too.
            Assert.Multiple(() =>
            {
                foreach (var part in Enum.GetValues<TargetBodyPart>())
                {
                    Assert.That(locale.HasString(WolfmedSyntheticHudLineSystem.PartKey(part)), Is.True,
                        $"no readout label for {part}.");
                }

                foreach (var key in new[] { "info", "warn", "crit", "fail" })
                    Assert.That(locale.HasString($"wolfmed-synthetic-tag-{key}"), Is.True);

                foreach (var key in new[]
                         {
                             "wolfmed-synthetic-system", "wolfmed-synthetic-diagnostics", "wolfmed-synthetic-glyph",
                             "wolfmed-synthetic-row-integrity", "wolfmed-synthetic-row-power",
                             "wolfmed-synthetic-row-fluid", "wolfmed-synthetic-row-servo",
                             "wolfmed-synthetic-row-faults", "wolfmed-synthetic-advice",
                             "wolfmed-synthetic-banner-integrity", "wolfmed-synthetic-banner-downed",
                             "wolfmed-synthetic-banner-standby", "wolfmed-synthetic-banner-reboot",
                             "wolfmed-synthetic-banner-panic", "wolfmed-synthetic-panic-dump",
                             "wolfmed-synthetic-death-banner", "wolfmed-synthetic-death-banner-sub",
                         })
                    Assert.That(locale.HasString(key), Is.True, $"missing readout string: {key}");

                for (var i = 1; i <= 6; i++)
                    Assert.That(locale.HasString($"wolfmed-synthetic-idle-{i}"), Is.True);
            });
        });
    }

    /// <summary>
    /// The readout never covers the game HUD. It takes no input, so it cannot steal a click, but it can
    /// still hide the hotbar, the alerts column, the chat pane or the targeting doll.
    /// </summary>
    [Test]
    public void LayoutKeepsOutOfTheHudTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (width, height) in new[] { (1920f, 1080f), (1280f, 720f) })
            {
                foreach (var scale in new[] { 1f, 1.4f })
                {
                    var screen = new UIBox2(0f, 0f, width, height);
                    var lines = WolfmedSyntheticHudLayout.MaxLines(screen, scale,
                        WolfmedSyntheticHudComponent.MaxFaults);
                    Assert.That(lines, Is.GreaterThan(3), $"{width}x{height} at {scale} shows almost no faults.");

                    var blocks = new (string Name, UIBox2 Box)[]
                    {
                        ("system", WolfmedSyntheticHudLayout.System(screen, scale)),
                        ("diagnostics", WolfmedSyntheticHudLayout.Diagnostics(screen, scale, lines)),
                        ("banner", WolfmedSyntheticHudLayout.Banner(screen, scale)),
                    };

                    foreach (var (name, box) in blocks)
                    {
                        Assert.That(box.Left, Is.GreaterThanOrEqualTo(screen.Left));
                        Assert.That(box.Right, Is.LessThanOrEqualTo(screen.Right));
                        Assert.That(box.Bottom, Is.LessThanOrEqualTo(screen.Top + height * WolfmedSyntheticHudLayout.TopBand),
                            $"{name} at {width}x{height} reaches below the readout's own band.");

                        foreach (var (reserved, area) in WolfmedSyntheticHudLayout.Reserved(screen))
                        {
                            Assert.That(WolfmedSyntheticHudLayout.Overlaps(box, area), Is.False,
                                $"{name} covers the {reserved} at {width}x{height} scale {scale}.");
                        }
                    }

                    // And the two corner blocks never run into each other or into the banner.
                    Assert.That(WolfmedSyntheticHudLayout.Overlaps(blocks[0].Box, blocks[1].Box), Is.False);
                    Assert.That(WolfmedSyntheticHudLayout.Overlaps(blocks[0].Box, blocks[2].Box), Is.False);
                    Assert.That(WolfmedSyntheticHudLayout.Overlaps(blocks[1].Box, blocks[2].Box), Is.False);
                }
            }
        });
    }

    private static EntityUid Part(
        IEntityManager entities,
        EntityUid body,
        BodyPartType type,
        BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }
}
