using Content.Shared._WF.PlanetCracker.Survey;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._WF.PlanetCracker.Survey;

/// <summary>Shows <see cref="WFDeepVeinOverlay"/> while the local player has <see cref="WFSurveyedComponent"/>.</summary>
public sealed partial class WFDeepVeinOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private IPlayerManager _player = default!;

    private WFDeepVeinOverlay _overlay = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFSurveyedComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WFSurveyedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WFSurveyedComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<WFSurveyedComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        _overlay = new WFDeepVeinOverlay();
    }

    /// <inheritdoc/>
    public override void Shutdown()
    {
        base.Shutdown();

        _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnInit(Entity<WFSurveyedComponent> ent, ref ComponentInit args)
    {
        if (_player.LocalEntity == ent.Owner)
            _overlayMan.AddOverlay(_overlay);

        RefreshVisuals();
    }

    private void OnShutdown(Entity<WFSurveyedComponent> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity == ent.Owner)
            _overlayMan.RemoveOverlay(_overlay);

        RefreshVisuals();
    }

    private void OnPlayerAttached(Entity<WFSurveyedComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        _overlayMan.AddOverlay(_overlay);

        RefreshVisuals();
    }

    private void OnPlayerDetached(Entity<WFSurveyedComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        _overlayMan.RemoveOverlay(_overlay);

        RefreshVisuals();
    }

    /// <summary>Keeps per-player sprite visibility in step with the overlay's lifetime.</summary>
    private void RefreshVisuals()
    {
        EntityManager.System<WFDeepVeinVisualsSystem>().RefreshAll();
    }
}
