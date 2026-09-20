using System.Numerics;
using Content.Shared._WF.PlanetCracker.Parachute;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._WF.PlanetCracker.Parachute;

/// <summary>Draws the open canopy over anything falling under a parachute.</summary>
public sealed class WFParachuteVisualsSystem : EntitySystem
{
    private static readonly SpriteSpecifier Canopy =
        new SpriteSpecifier.Rsi(new ResPath("_WF/Effects/parachute_canopy.rsi"), "canopy");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFParachutedComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WFParachutedComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnState(Entity<WFParachutedComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var has = sprite.LayerMapTryGet(CanopyKey.Key, out var layer);

        if (!ent.Comp.Deployed)
        {
            if (has)
                sprite.RemoveLayer(layer);

            return;
        }

        if (has)
            return;

        layer = sprite.AddLayer(Canopy);
        sprite.LayerMapSet(CanopyKey.Key, layer);

        // The canopy art has its harness at the bottom edge; sit that on the wearer's top.
        sprite.LayerSetOffset(layer, new Vector2(0f, sprite.Bounds.Height / 2f + 0.5f));
    }

    private void OnShutdown(Entity<WFParachutedComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite) && sprite.LayerMapTryGet(CanopyKey.Key, out var layer))
            sprite.RemoveLayer(layer);
    }

    private enum CanopyKey
    {
        Key,
    }
}
