using Content.Shared._WF.PlanetCracker.Anchors;
using Robust.Client.Graphics;

namespace Content.Client._WF.PlanetCracker.Anchors;

/// <summary>Owns the crack circle overlay and drops its cached rings whenever an anchor's state arrives from the server.</summary>
public sealed partial class WFCrackCircleOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    private WFCrackCircleOverlay _circles = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        _circles = new WFCrackCircleOverlay();
        _overlay.AddOverlay(_circles);

        SubscribeLocalEvent<WFGravityAnchorComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WFGravityAnchorComponent, ComponentShutdown>(OnShutdown);
    }

    /// <inheritdoc/>
    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<WFCrackCircleOverlay>();
    }

    /// <summary>Drops only the updated pair's cached ring; progress changes often and is not cached.</summary>
    private void OnState(Entity<WFGravityAnchorComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _circles.Invalidate(ent.Owner);

        // The ring is cached against the lower-uid half, which may be the partner rather than the anchor that dirtied.
        if (ent.Comp.Partner is { } netPartner && TryGetEntity(netPartner, out var partner))
            _circles.Invalidate(partner.Value);
    }

    /// <summary>An anchor leaving the client's view takes its cached ring with it.</summary>
    private void OnShutdown(Entity<WFGravityAnchorComponent> ent, ref ComponentShutdown args)
    {
        _circles.Invalidate(ent.Owner);
    }
}
