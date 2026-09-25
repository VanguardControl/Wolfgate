using Robust.Client.Graphics;

namespace Content.Client._WF.PlanetCracker.Chunk;

/// <summary>Owns the crack beam overlay; the overlay caches nothing, so there is nothing to invalidate.</summary>
public sealed partial class WFCrackBeamOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        _overlay.AddOverlay(new WFCrackBeamOverlay());
    }

    /// <inheritdoc/>
    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<WFCrackBeamOverlay>();
    }
}
