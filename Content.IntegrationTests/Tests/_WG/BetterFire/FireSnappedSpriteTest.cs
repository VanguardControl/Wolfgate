#nullable enable
using Content.Client.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.BetterFire;

/// <summary>
/// A machine whose sprite snaps to cardinals can burn with the four-directional mob fire: the fire layer turns on its
/// own, and the sprite's bounds still work out. Before, working them out failed a debug assertion in the engine.
/// </summary>
[TestFixture]
[TestOf(typeof(FireVisualizerSystem))]
public sealed class FireSnappedSpriteTest
{
    private const string Machine = "WGBetterFireTestMachine";
    private const string Sign = "WGBetterFireTestSign";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGBetterFireTestMachine
  components:
  - type: Sprite
    sprite: Structures/Power/smes.rsi
    snapCardinals: true
    layers:
    - state: smes
  - type: Appearance
  - type: FireVisuals
    sprite: Mobs/Effects/onfire.rsi
    normalState: Generic_mob_burning

- type: entity
  id: WGBetterFireTestSign
  components:
  - type: Sprite
    sprite: Structures/Power/smes.rsi
    snapCardinals: true
    layers:
    - state: smes
  - type: Appearance
  - type: FireVisuals
    sprite: Effects/fire.rsi
    normalState: fire
";

    [Test]
    public async Task SnappedSpriteBurnsWithDirectionalFire()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entMan = client.EntMan;
        var sprites = client.System<SpriteSystem>();
        var appearance = client.System<AppearanceSystem>();

        EntityUid machine = default;
        EntityUid sign = default;
        await client.WaitPost(() =>
        {
            machine = entMan.SpawnEntity(Machine, MapCoordinates.Nullspace);
            sign = entMan.SpawnEntity(Sign, MapCoordinates.Nullspace);
        });
        await pair.RunTicksSync(2);

        await client.WaitPost(() =>
        {
            foreach (var uid in new[] { machine, sign })
            {
                appearance.SetData(uid, FireVisuals.OnFire, true);
                appearance.SetData(uid, FireVisuals.FireStacks, 2);
            }
        });
        await pair.RunTicksSync(2);

        await client.WaitAssertion(() =>
        {
            var sprite = entMan.GetComponent<SpriteComponent>(machine);
            Assert.That(sprites.LayerMapTryGet((machine, sprite), FireVisualLayers.Fire, out var fire, false));
            Assert.That(sprites.TryGetLayer((machine, sprite), fire, out var layer, false));
            Assert.That(layer!.Visible, "The machine's fire layer is not showing.");
            Assert.That(sprite.GranularLayersRendering, "The burning machine does not render its layers one by one.");
            Assert.That(layer.RenderingStrategy, Is.EqualTo(LayerRenderingStrategy.Default),
                "The directional fire layer still snaps to cardinals.");
            Assert.That(sprites.TryGetLayer((machine, sprite), 0, out var body, false));
            Assert.That(body!.RenderingStrategy, Is.EqualTo(LayerRenderingStrategy.UseSpriteStrategy),
                "The machine's own layer no longer snaps with its sprite.");
            // Fails a debug assertion when a snapped layer shows a state with more than one direction.
            Assert.DoesNotThrow(() => sprites.GetLocalBounds((machine, sprite)));

            // One-directional fire has no need to turn, and keeps snapping.
            var signSprite = entMan.GetComponent<SpriteComponent>(sign);
            Assert.That(signSprite.GranularLayersRendering, Is.False,
                "A snapped sprite with one-directional fire was switched to rendering its layers one by one.");
            Assert.DoesNotThrow(() => sprites.GetLocalBounds((sign, signSprite)));
        });

        await client.WaitPost(() =>
        {
            entMan.DeleteEntity(machine);
            entMan.DeleteEntity(sign);
        });

        await pair.CleanReturnAsync();
    }
}
