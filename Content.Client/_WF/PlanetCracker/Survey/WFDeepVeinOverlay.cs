using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Survey;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.PlanetCracker.Survey;

/// <summary>
/// Draws the Marker layer of every revealed vein inside the last pulse a second time, fading out over the surveyor's
/// ping window. The sprite itself is already visible thanks to <see cref="WFDeepVeinVisualsSystem"/>; this is the
/// cosmetic sweep that tells the crew which veins the pulse they just fired actually touched.
/// </summary>
public sealed partial class WFDeepVeinOverlay : Overlay
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;

    private readonly EntityLookupSystem _lookup;
    private readonly SpriteSystem _sprite;
    private readonly TransformSystem _xform;

    private readonly EntityQuery<SpriteComponent> _spriteQuery;
    private readonly EntityQuery<TransformComponent> _xformQuery;

    /// <summary>Reused hit buffer, so a per-frame draw does not allocate a new set each pass.</summary>
    private readonly HashSet<Entity<WFDeepVeinComponent>> _veins = new();

    /// <inheritdoc/>
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    /// <inheritdoc/>
    public override bool RequestScreenTexture => false;

    public WFDeepVeinOverlay()
    {
        IoCManager.InjectDependencies(this);

        _lookup = _entityManager.System<EntityLookupSystem>();
        _sprite = _entityManager.System<SpriteSystem>();
        _xform = _entityManager.System<TransformSystem>();

        _spriteQuery = _entityManager.GetEntityQuery<SpriteComponent>();
        _xformQuery = _entityManager.GetEntityQuery<TransformComponent>();
    }

    /// <inheritdoc/>
    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;

        if (_player.LocalEntity is not { } localEntity ||
            !_entityManager.TryGetComponent<WFSurveyedComponent>(localEntity, out var surveyed) ||
            surveyed.LastPulse is not { } lastPulse)
            return;

        var remaining = (float)(surveyed.PulseFadeEnd - _timing.CurTime).TotalSeconds;
        var window = (float)surveyed.PulseFade.TotalSeconds;

        if (remaining <= 0f || window <= 0f)
            return;

        var alpha = Math.Clamp(remaining / window, 0f, 1f);

        // The skin stores sRGB hex and DrawTexture writes straight into Vertex2D.Modulate, which is linear, so any
        // skin colour used as a modulate goes through Color.FromSrgb (WFDiagramControl.cs:1-13 is authoritative).
        var skin = WolfgateSkins.Get(_cfg.GetCVar(WolfgateCVars.UiStyle));
        var modulate = Color.FromSrgb(skin.Accent).WithAlpha(alpha);

        var coordinates = _entityManager.GetCoordinates(lastPulse);
        var scaleMatrix = Matrix3Helpers.CreateScale(Vector2.One);

        _veins.Clear();

        // Uncontained matches the server's scan: an anchored bodyless vein rides the DYNAMIC SundriesTree, so the
        // Sundries bit is the one that finds it and the Static bit contributes nothing.
        _lookup.GetEntitiesInRange(coordinates, surveyed.LastPulseRadius, _veins, LookupFlags.Uncontained);

        foreach (var vein in _veins)
        {
            if (!surveyed.Revealed.Contains(_entityManager.GetNetEntity(vein.Owner)))
                continue;

            if (!_xformQuery.TryComp(vein.Owner, out var xform) ||
                !_spriteQuery.TryComp(vein.Owner, out var sprite))
                continue;

            // During a z-pass args.MapId is the map being rendered, so this is the whole layer filter needed.
            if (xform.MapID != args.MapId)
                continue;

            if (!_sprite.LayerMapTryGet((vein.Owner, sprite), WFDeepVeinVisualLayers.Marker, out var index, false))
                continue;

            var layer = sprite[index];

            if (layer.ActualRsi?.Path == null || layer.RsiState.Name == null)
                continue;

            var gridRot = xform.GridUid == null ? 0 : _xformQuery.CompOrNull(xform.GridUid.Value)?.LocalRotation ?? 0;
            var rotationMatrix = Matrix3Helpers.CreateRotation(gridRot);

            var worldMatrix = Matrix3Helpers.CreateTranslation(_xform.GetWorldPosition(xform));
            var scaledWorld = Matrix3x2.Multiply(scaleMatrix, worldMatrix);
            handle.SetTransform(Matrix3x2.Multiply(rotationMatrix, scaledWorld));

            var spriteSpec = new SpriteSpecifier.Rsi(layer.ActualRsi.Path, layer.RsiState.Name);
            var texture = _sprite.GetFrame(spriteSpec, TimeSpan.FromSeconds(layer.AnimationTime));

            handle.DrawTexture(texture,
                -(Vector2)texture.Size / 2f / EyeManager.PixelsPerMeter,
                layer.Rotation,
                modulate: modulate);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
