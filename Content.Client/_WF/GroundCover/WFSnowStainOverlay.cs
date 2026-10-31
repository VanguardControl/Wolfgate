using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WF.GroundCover;

/// <summary>Draws stains over the snow cover, under footprints and entities.</summary>
public sealed class WFSnowStainOverlay : Overlay
{
    [Dependency] private IEntityManager _entMan = default!;

    private readonly WFSnowStainSystem _stains;
    private readonly WFGroundCoverSystem _cover;
    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public WFSnowStainOverlay(WFSnowStainSystem stains)
    {
        IoCManager.InjectDependencies(this);
        _stains = stains;
        _cover = _entMan.System<WFGroundCoverSystem>();
        _transform = _entMan.System<SharedTransformSystem>();
        // Over the snow cover the ground cover overlay draws at 10, under the footprints at 12.
        ZIndex = 11;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _stains.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        foreach (var (grid, tiles) in _stains.Stains)
        {
            if (!_entMan.TryGetComponent(grid, out TransformComponent? xform) || xform.MapID != args.MapId)
                continue;

            var (_, _, matrix, inverse) = _transform.GetWorldPositionRotationMatrixWithInv(grid);
            var bounds = inverse.TransformBox(args.WorldAABB).Enlarged(1f);
            handle.SetTransform(matrix);
            foreach (var (tile, stain) in tiles)
            {
                if (!bounds.Contains(tile + new Vector2(0.5f, 0.5f))
                    || _cover.IsUnderVessel(args.MapId, Vector2.Transform(tile + new Vector2(0.5f, 0.5f), matrix)))
                    continue;

                var alpha = _stains.Visibility(stain) * WFSnowStainSystem.Opacity;
                if (alpha <= 0.01f)
                    continue;

                // A soft blot: a wide faint disc with a stronger heart.
                var colour = stain.Color.WithAlpha(1f);
                handle.DrawCircle(stain.Position, stain.Radius, colour.WithAlpha(alpha * 0.45f));
                handle.DrawCircle(stain.Position, stain.Radius * 0.72f, colour.WithAlpha(alpha * 0.7f));
                handle.DrawCircle(stain.Position, stain.Radius * 0.45f, colour.WithAlpha(alpha));
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
