using Content.Shared._WF.AsteroidMining;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.AsteroidMining;

/// <summary>Registers the beam overlay and spins the gun's barrel while it fires.</summary>
public sealed partial class WFMiningBeamVisualizerSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string IdleState = "space_artillery";
    private const string FiringState = "firing";

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WFMiningBeamOverlay(EntityManager, _prototypes, _timing));
        SubscribeLocalEvent<WFMiningBeamEmitterComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFMiningBeamEmitterComponent, AfterAutoHandleStateEvent>(OnState);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WFMiningBeamOverlay>();
        base.Shutdown();
    }

    private void OnStartup(Entity<WFMiningBeamEmitterComponent> ent, ref ComponentStartup args)
    {
        UpdateBarrel(ent);
    }

    private void OnState(Entity<WFMiningBeamEmitterComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateBarrel(ent);
    }

    private void UpdateBarrel(Entity<WFMiningBeamEmitterComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite) ||
            !_sprite.LayerMapTryGet((ent, sprite), WFMiningBeamVisualLayers.Base, out var layer, false))
            return;

        var state = ent.Comp.Active ? FiringState : IdleState;
        if (sprite[layer].RsiState.Name != state)
            _sprite.LayerSetRsiState((ent, sprite), layer, state);
    }
}
