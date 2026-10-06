using System.Collections.Generic;
using Content.Client._WF.CustomMarkings;
using Content.Client.Clickable;
using Content.Client.Humanoid;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.IntegrationTests.Tests._WF.CustomMarkings;

/// <summary>Art drawn on the body loses the pixels over a body part that is hidden, and gets them back with it.</summary>
[TestFixture]
[TestOf(typeof(CustomMarkingSystem))]
public sealed class CustomMarkingLimbsTest
{
    private const int Frame = CustomMarkingRules.FrameSize;

    private static readonly string Hash = new('c', CustomMarkingRules.HashLength);
    private static readonly Rgba32 Red = new(255, 0, 0, 255);

    /// <summary>The body parts that lie under art drawn on the skin, which sits below the hands and feet.</summary>
    private static readonly HumanoidVisualLayers[] UnderSkinArt =
    {
        HumanoidVisualLayers.Chest,
        HumanoidVisualLayers.Head,
        HumanoidVisualLayers.RArm,
        HumanoidVisualLayers.LArm,
        HumanoidVisualLayers.RLeg,
        HumanoidVisualLayers.LLeg,
    };

    [Test]
    public async Task LostLimbTakesItsArtTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var entMan = client.EntMan;
            var sprites = entMan.System<SpriteSystem>();
            var humanoids = entMan.System<HumanoidAppearanceSystem>();
            var system = entMan.System<CustomMarkingSystem>();
            var clickMap = client.ResolveDependency<IClickMapManager>();
            var resources = CustomMarkingResources.For(client.ResolveDependency<IResourceCache>());

            // A drawing that covers every pixel of every facing, so whatever is cut out shows.
            var art = new CustomMarkingArt();
            for (var facing = 0; facing < CustomMarkingRules.Facings; facing++)
            {
                for (var y = 0; y < Frame; y++)
                {
                    for (var x = 0; x < Frame; x++)
                    {
                        art.SetPixel(facing, x, y, Red);
                    }
                }
            }

            resources.Store(Hash, art.ToPng());

            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
            var species = client.ResolveDependency<IPrototypeManager>().Index<SpeciesPrototype>(profile.Species);
            var doll = entMan.SpawnEntity(species.DollPrototype, MapCoordinates.Nullspace);
            humanoids.LoadProfile(doll, profile.WithCustomMarkings(new List<CustomMarking> { new(Hash, CustomMarkingPlacement.Skin) }));

            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(doll);
            var sprite = new Entity<SpriteComponent>(doll, entMan.GetComponent<SpriteComponent>(doll));

            Assert.That(system.TryGetArt(Hash, out var whole), Is.True);
            Assert.That(Shown().ActualRsi, Is.SameAs(whole), "a whole body wears the drawing as it is");

            // The mirror for drawing stands on the body's middle column, front and back.
            Assert.That(system.MirrorAxis((sprite.Owner, sprite.Comp), CustomMarkingArt.South), Is.EqualTo(CustomMarkingSketch.DefaultMirrorAxis));
            Assert.That(system.MirrorAxis((sprite.Owner, sprite.Comp), CustomMarkingArt.North), Is.EqualTo(CustomMarkingSketch.DefaultMirrorAxis));

            // The left arm goes.
            humanoids.SetLayerVisibility((doll, humanoid), HumanoidVisualLayers.LArm, false);
            system.Refresh((doll, humanoid));
            var cutRsi = Shown().ActualRsi;
            Assert.That(cutRsi, Is.Not.SameAs(whole), "a body missing a part wears a cut copy");

            var name = cutRsi.Path.FilenameWithoutExtension;
            Assert.That(resources.TryGetPng(name, out var png), Is.True);
            var cut = CustomMarkingPng.Read(png);
            Assert.That(cut, Is.Not.Null);

            var gone = 0;
            for (var facing = 0; facing < CustomMarkingRules.Facings; facing++)
            {
                for (var y = 0; y < Frame; y++)
                {
                    for (var x = 0; x < Frame; x++)
                    {
                        var missing = cut.GetPixel(facing, x, y).A == 0;
                        var onArm = TopPart(facing, x, y) == HumanoidVisualLayers.LArm;
                        if (missing)
                            gone++;

                        Assert.That(missing, Is.EqualTo(onArm), $"facing {facing}, pixel {x},{y}: only what lay on the arm goes");
                    }
                }
            }

            Assert.That(gone, Is.GreaterThan(0), "the arm had art on it");
            Assert.That(cut.GetPixel(CustomMarkingArt.South, 0, 0), Is.EqualTo(Red), "art off the body stays");

            // The same body again reuses the copy rather than cutting a new one.
            system.Refresh((doll, humanoid));
            Assert.That(Shown().ActualRsi, Is.SameAs(cutRsi));

            // The arm comes back, and with it the whole drawing.
            humanoids.SetLayerVisibility((doll, humanoid), HumanoidVisualLayers.LArm, true);
            system.Refresh((doll, humanoid));
            Assert.That(Shown().ActualRsi, Is.SameAs(whole));

            // Art behind the body isn't on any part of it, so it is never cut.
            humanoids.LoadProfile(doll, profile.WithCustomMarkings(new List<CustomMarking> { new(Hash, CustomMarkingPlacement.Behind) }));
            humanoids.SetLayerVisibility((doll, humanoid), HumanoidVisualLayers.LArm, false);
            system.Refresh((doll, humanoid));
            Assert.That(Shown().ActualRsi, Is.SameAs(whole));

            entMan.DeleteEntity(doll);
            return;

            SpriteComponent.Layer Shown()
            {
                Assert.That(sprites.LayerMapTryGet((sprite.Owner, sprite.Comp), CustomMarkingSystem.LayerKey(0), out var index, false), Is.True);
                return (SpriteComponent.Layer) sprite.Comp[index];
            }

            // The topmost body part under the art that is opaque at a pixel, or null where there is none.
            HumanoidVisualLayers? TopPart(int facing, int x, int y)
            {
                HumanoidVisualLayers? top = null;
                var topIndex = -1;
                foreach (var part in UnderSkinArt)
                {
                    if (!sprites.LayerMapTryGet((sprite.Owner, sprite.Comp), part, out var index, false)
                        || index < topIndex
                        || sprite.Comp[index] is not SpriteComponent.Layer { ActualRsi: { } rsi } layer
                        || !rsi.TryGetState(layer.State, out var state))
                        continue;

                    var direction = state.RsiDirections == RsiDirectionType.Dir1 ? RsiDirection.South : (RsiDirection) facing;
                    if (!clickMap.IsOpaque(rsi, layer.State, direction, 0, new Vector2i(x, y)))
                        continue;

                    top = part;
                    topIndex = index;
                }

                return top;
            }
        });

        await pair.CleanReturnAsync();
    }
}
