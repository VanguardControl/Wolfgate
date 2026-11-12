using System.Collections.Generic;
using System.Numerics;
using Content.Client._WF.AppearanceStudio;
using Content.Client.Humanoid;
using Content.Shared._WF.AppearanceStudio;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.AppearanceStudio;

/// <summary>A doll's body parts move, scale and hide for the facing it shows, and go back when the tweaks are taken off.</summary>
[TestFixture]
[TestOf(typeof(StudioSpriteSystem))]
public sealed class StudioSpriteTest
{
    [Test]
    public async Task DollIsPlacedPerFacingTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var entMan = client.EntMan;
            var studio = entMan.System<StudioSpriteSystem>();
            var sprites = entMan.System<SpriteSystem>();
            var humanoids = entMan.System<HumanoidAppearanceSystem>();

            var plain = HumanoidCharacterProfile.DefaultWithSpecies();
            var species = client.ResolveDependency<IPrototypeManager>().Index<SpeciesPrototype>(plain.Species);
            var doll = entMan.SpawnEntity(species.DollPrototype, MapCoordinates.Nullspace);
            humanoids.LoadProfile(doll, plain);
            var sprite = new Entity<SpriteComponent>(doll, entMan.GetComponent<SpriteComponent>(doll));

            Assert.That(sprites.LayerMapTryGet(sprite.AsNullable(), HumanoidVisualLayers.Head, out var head, false), Is.True);
            Assert.That(sprites.LayerMapTryGet(sprite.AsNullable(), HumanoidVisualLayers.LArm, out var arm, false), Is.True);
            var baseOffset = ((SpriteComponent.Layer) sprite.Comp[head]).Offset;
            var baseScale = ((SpriteComponent.Layer) sprite.Comp[head]).Scale;

            var tweaks = new List<StudioTweak>
            {
                new(StudioTarget.Body, nameof(HumanoidVisualLayers.Head))
                {
                    Offsets = new[] { new Vector2i(0, -5), new Vector2i(2, 3), Vector2i.Zero, Vector2i.Zero },
                    Scale = 1.5f,
                },
                new(StudioTarget.Body, nameof(HumanoidVisualLayers.LArm)) { Hidden = true },
            };

            humanoids.LoadProfile(doll, plain.WithStudioTweaks(tweaks));
            studio.SetPreviewFacing(doll, Direction.South);
            studio.FrameUpdate(0f);

            Assert.Multiple(() =>
            {
                Assert.That(((SpriteComponent.Layer) sprite.Comp[head]).Offset, Is.EqualTo(baseOffset + new Vector2(0, -5) / 32f));
                Assert.That(((SpriteComponent.Layer) sprite.Comp[head]).Scale, Is.EqualTo(baseScale * 1.5f));
                Assert.That(sprite.Comp[arm].Visible, Is.False, "a hidden part isn't drawn");
            });

            studio.SetPreviewFacing(doll, Direction.North);
            studio.FrameUpdate(0f);
            Assert.That(((SpriteComponent.Layer) sprite.Comp[head]).Offset, Is.EqualTo(baseOffset + new Vector2(2, 3) / 32f), "north has its own offset");

            // Running again changes nothing: offsets are applied once, not added up each frame.
            studio.FrameUpdate(0f);
            Assert.That(((SpriteComponent.Layer) sprite.Comp[head]).Offset, Is.EqualTo(baseOffset + new Vector2(2, 3) / 32f));

            humanoids.LoadProfile(doll, plain);
            studio.FrameUpdate(0f);
            Assert.Multiple(() =>
            {
                Assert.That(((SpriteComponent.Layer) sprite.Comp[head]).Offset, Is.EqualTo(baseOffset), "taking the tweaks off puts the part back");
                Assert.That(((SpriteComponent.Layer) sprite.Comp[head]).Scale, Is.EqualTo(baseScale));
            });

            entMan.DeleteEntity(doll);
        });

        await pair.CleanReturnAsync();
    }
}
