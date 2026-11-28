using System.Collections.Generic;
using System.Linq;
using Content.Shared._WG.Modsuits;
using Content.Shared._WG.Modsuits.Components;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Modsuits;

/// <summary>Checks the generated plate patterns cover every control unit and point at prototypes that exist.</summary>
[TestFixture]
[TestOf(typeof(ModPatternPrototype))]
public sealed class ModPatternTest
{
    [Test]
    public async Task EveryControlUnitHasAPattern()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var patterns = protoMan.EnumeratePrototypes<ModPatternPrototype>().ToList();
            Assert.That(patterns, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.Abstract || !proto.TryComp<ModSuitComponent>(out var suit, factory))
                        continue;

                    var parts = new Dictionary<string, EntProtoId>(suit.Parts);
                    var theme = suit.Theme;
                    Assert.That(patterns.Any(p => p.Theme == theme && SameParts(p.Parts, parts)),
                        $"{proto.ID} has no modPattern with its theme and parts; run Tools/_WG/Modsuits/patterns.py");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PatternsPointAtRealPrototypes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var tags = server.System<TagSystem>();

        await server.WaitAssertion(() =>
        {
            var entities = protoMan.EnumeratePrototypes<EntityPrototype>().Where(p => !p.Abstract).ToList();

            Assert.Multiple(() =>
            {
                foreach (var pattern in protoMan.EnumeratePrototypes<ModPatternPrototype>())
                {
                    Assert.That(protoMan.HasIndex(pattern.Theme), $"{pattern.ID}: theme {pattern.Theme} does not exist");
                    Assert.That(pattern.Parts, Has.Count.EqualTo(4), $"{pattern.ID} should name four parts");

                    foreach (var (slot, part) in pattern.Parts)
                    {
                        Assert.That(protoMan.HasIndex(part), $"{pattern.ID}: {slot} part {part} does not exist");
                    }

                    foreach (var module in pattern.InbuiltModules)
                    {
                        Assert.That(protoMan.TryIndex(module, out var moduleProto)
                                    && moduleProto.TryComp<ModModuleComponent>(out _, factory),
                            $"{pattern.ID}: inbuilt module {module} does not exist or is not a module");
                    }

                    Assert.That(protoMan.TryIndex(pattern.Source, out var source)
                                && source.TryComp<ModSuitComponent>(out _, factory),
                        $"{pattern.ID}: source {pattern.Source} is not a control unit");

                    if (pattern.Plating is not { } plating)
                        continue;

                    Assert.That(protoMan.HasIndex(plating), $"{pattern.ID}: plating tag {plating} does not exist");
                    Assert.That(entities.Any(e => e.TryComp<TagComponent>(out var tag, factory) && tags.HasTag(tag, plating)),
                        $"{pattern.ID}: no item carries plating tag {plating}");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    private static bool SameParts(Dictionary<string, EntProtoId> a, Dictionary<string, EntProtoId> b)
    {
        return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var other) && other == kv.Value);
    }
}
