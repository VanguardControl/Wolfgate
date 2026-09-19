#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Wounds;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W7: every string the wound system can put in front of a player resolves. Wound names and stage names
/// come from prototypes; the analyzer's findings are built by string concatenation in
/// <c>WolfmedDiagnosticPanel</c>, so a new enum member there is a missing key nothing else catches.
/// </summary>
/// <remarks>
/// The key families mirror the panel one for one. Both the organic form and the <c>-mechanical</c> /
/// <c>-frame</c> form are required wherever the panel can pick either, which is what makes this the test
/// that keeps a chassis from reading as flesh.
/// </remarks>
[TestFixture]
public sealed class WolfmedLocaleCoverageTest : GameTest
{
    /// <summary>
    /// Enum-keyed families the panel builds: prefix, the enum type, and the members it never renders.
    /// </summary>
    private static readonly (string Prefix, Type Enum, string[] Skipped, bool Mechanical)[] Families =
    [
        ("fracture-grade-", typeof(FractureGrade), ["None"], false),
        ("health-analyzer-wound-fracture-treatment-", typeof(FractureTreatment), ["None"], false),
        // NotApplicable and None are the two phases the panel skips: it renders only the three that mean
        // something to a medic.
        ("health-analyzer-wound-clotting-", typeof(HealthAnalyzerClottingPhase),
            ["NotApplicable", "None"], true),
        ("health-analyzer-wound-infection-", typeof(WolfmedInfectionStage), ["None"], false),
        ("health-analyzer-wound-functionality-", typeof(BodyPartFunctionalityState), ["Functional"], false),
    ];

    /// <summary>Fixed keys the panel uses, with the suffixes it can append to each.</summary>
    private static readonly (string Key, string[] Suffixes)[] Fixed =
    [
        ("health-analyzer-wound-part-summary", [""]),
        ("health-analyzer-wound-diagnostics-inactive", [""]),
        ("health-analyzer-wound-diagnostics-unavailable", [""]),
        ("health-analyzer-wound-blood-level-dangerous", [""]),
        ("health-analyzer-wound-bleeding-short", ["", "-mechanical"]),
        ("health-analyzer-wound-pain-short", ["", "-mechanical"]),
        ("health-analyzer-wound-fracture-short", ["", "-frame"]),
        ("health-analyzer-wound-fracture-treated-short", ["", "-frame"]),
        ("health-analyzer-wound-internal-bleeding-short", [""]),
        ("health-analyzer-wound-overheating-short", [""]),
        ("health-analyzer-wound-embedded-short", [""]),
        ("health-analyzer-wound-necrotic-short", [""]),
        ("health-analyzer-wound-necrosis-risk-short", [""]),
        ("health-analyzer-wound-scars-short", [""]),
        ("health-analyzer-wound-sepsis", [""]),
    ];

    /// <summary>Every wound prototype names itself and its stages in a locale file.</summary>
    [Test]
    public async Task EveryWoundNameResolvesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var wound in prototypes.EnumeratePrototypes<WoundPrototype>().OrderBy(wound => wound.ID))
                {
                    Assert.That(locale.HasString(wound.Name.Id), Is.True,
                        $"{wound.ID} has no name string ({wound.Name.Id}).");
                    foreach (var (key, stage) in wound.Stages)
                    {
                        Assert.That(locale.HasString(stage.Name.Id), Is.True,
                            $"{wound.ID} stage {key} has no name string ({stage.Name.Id}).");
                    }
                }
            });
        });
    }

    /// <summary>
    /// Every key the analyzer panel can build, in both the organic and the mechanical form.
    /// </summary>
    [Test]
    public async Task EveryAnalyzerFindingResolvesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var locale = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var required = new List<string>();
            foreach (var (key, suffixes) in Fixed)
                required.AddRange(suffixes.Select(suffix => key + suffix));

            foreach (var (prefix, type, skipped, mechanical) in Families)
            {
                foreach (var name in Enum.GetNames(type).Where(name => !skipped.Contains(name)))
                {
                    required.Add(prefix + name.ToLowerInvariant());
                    if (mechanical)
                        required.Add(prefix + name.ToLowerInvariant() + "-mechanical");
                }
            }

            Assert.Multiple(() =>
            {
                foreach (var key in required)
                    Assert.That(locale.HasString(key), Is.True, $"missing analyzer string: {key}");
            });
        });
    }
}
