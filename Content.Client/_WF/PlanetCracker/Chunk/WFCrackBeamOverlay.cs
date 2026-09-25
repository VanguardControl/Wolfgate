using System.Numerics;
using Content.Client._WF.PlanetCracker.Anchors;
using Content.Client._WF.Stylesheets;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.PlanetCracker.Chunk;

/// <summary>Draws each firing projector's beam to its anchor across z-levels, and from surface anchors up to the mount.</summary>
public sealed partial class WFCrackBeamOverlay : Overlay
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private IPlayerManager _player = default!;

    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprite;
    private readonly ISawmill _sawmill;

    private readonly EntityQuery<TransformComponent> _xformQuery;
    private readonly EntityQuery<CEZMapComponent> _zMapQuery;
    private readonly EntityQuery<WFGravityAnchorComponent> _anchorQuery;

    /// <summary>Greyscale horizontal beam frame; an atlas region, so it is stretched along X and never UV-tiled.</summary>
    private static readonly SpriteSpecifier BeamSprite = new SpriteSpecifier.Rsi(
        new ResPath("/Textures/_Mono/Objects/Weapons/Guns/Projectiles/lasers.rsi"), "grayscale_beam");

    /// <summary>Muzzle offset in the projector's rotated frame, in tiles; the barrels end 26 px below the sprite's centre.</summary>
    private static readonly Vector2 EmitterOffset = new(0f, -0.8125f);

    /// <summary>Shorter than this and the rect degenerates, so the beam is simply not drawn.</summary>
    private const float MinBeamLength = 0.05f;

    // Each bail-out logs once, so it can be told apart from a swallowed exception.
    private bool _loggedNoEye;

    private bool _loggedNoDepth;

    /// <inheritdoc/>
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WFCrackBeamOverlay()
    {
        IoCManager.InjectDependencies(this);

        _transform = _entityManager.System<SharedTransformSystem>();
        _sprite = _entityManager.System<SpriteSystem>();
        _sawmill = _logManager.GetSawmill("wf.crackbeam");

        _xformQuery = _entityManager.GetEntityQuery<TransformComponent>();
        _zMapQuery = _entityManager.GetEntityQuery<CEZMapComponent>();
        _anchorQuery = _entityManager.GetEntityQuery<WFGravityAnchorComponent>();

        // Equal ZIndex draws in random order, so the beam takes its own slot above the ring.
        ZIndex = 10;
    }

    /// <inheritdoc/>
    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);

        // Draw once, in the viewer's own map pass. `eye is ZEye` is the wrong test: altitude zero gets a plain eye.
        if (_player.LocalEntity is not { } local ||
            !_xformQuery.TryComp(local, out var localXform) ||
            localXform.MapID != args.MapId)
        {
            return;
        }

        // Use this pass's eye; at non-zero altitude it is a scaled ZEye, not IEyeManager.CurrentEye.
        if (args.Viewport.Eye is not { } eye)
        {
            if (!_loggedNoEye)
            {
                _loggedNoEye = true;
                _sawmill.Warning("Beam overlay ran on a viewport with no eye; no beams drawn.");
            }

            return;
        }

        if (!_zMapQuery.TryComp(args.MapUid, out var viewerMap))
        {
            if (!_loggedNoDepth)
            {
                _loggedNoDepth = true;
                _sawmill.Debug($"Beam overlay skipped: viewer map {args.MapUid} carries no CEZMapComponent.");
            }

            return;
        }

        var eyePos = eye.Position.Position;
        var eyeOffset = eye.Offset;
        var up = (-eye.Rotation).ToWorldVec();

        var skin = WolfgateSkins.Get(_cfg.GetCVar(WolfgateCVars.UiStyle));

        var texture = _sprite.GetFrame(BeamSprite, _timing.CurTime);

        // The line runs along X, so its thickness is the frame's height.
        var width = texture.Height / (float) EyeManager.PixelsPerMeter;

        var query = _entityManager
            .EntityQueryEnumerator<WFCrackBeamComponent, WFGravityProjectorComponent, TransformComponent>();
        while (query.MoveNext(out var beam, out _, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            // Anchors carry a global PVS override, so the target always resolves.
            if (beam.Target is not { } netTarget ||
                !_entityManager.TryGetEntity(netTarget, out var target) ||
                !_xformQuery.TryComp(target, out var targetXform) ||
                !_anchorQuery.TryComp(target, out var anchor))
            {
                continue;
            }

            if (targetXform.MapUid is not { } targetMapUid || !_zMapQuery.TryComp(targetMapUid, out var targetMap))
            {
                if (!_loggedNoDepth)
                {
                    _loggedNoDepth = true;
                    _sawmill.Debug($"Beam overlay skipped a beam: target map of {target} carries no CEZMapComponent.");
                }

                continue;
            }

            // Depth counts upward, so n is how many layers below the viewer the anchor is.
            var n = viewerMap.Depth - targetMap.Depth;
            var r = MathF.Pow(CESharedZLevelsSystem.ZLevelViewShrink, n);

            var anchorPos = _transform.GetWorldPosition(targetXform);

            // Project the anchor into this pass: each layer's scale and offset fold into one affine map about the eye.
            var far = eyePos + eyeOffset
                + r * (anchorPos - eyePos - eyeOffset + up * CESharedZLevelsSystem.ZLevelOffset * n);

            var near = _transform.GetWorldPosition(xform)
                + _transform.GetWorldRotation(xform).RotateVec(EmitterOffset);

            DrawBeam(handle, texture, near, far, width, ColourOf(skin, anchor));
        }

        // Surface half: the projector is outside a surface viewer's PVS, so the anchor carries the mount's world XY.
        var surface = _entityManager
            .EntityQueryEnumerator<WFCrackBeamTargetComponent, WFGravityAnchorComponent, TransformComponent>();
        while (surface.MoveNext(out var target, out var anchor, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            DrawBeam(
                handle,
                texture,
                _transform.GetWorldPosition(xform),
                target.ProjectorWorldPos,
                width,
                ColourOf(skin, anchor));
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>The pair's own ring colour, which both halves of a beam take; an unpaired anchor stands in for itself.</summary>
    private Color ColourOf(WolfgateSkin skin, WFGravityAnchorComponent anchor)
    {
        var partner = anchor.Partner is { } netPartner &&
                      _entityManager.TryGetEntity(netPartner, out var partnerUid) &&
                      _anchorQuery.TryComp(partnerUid, out var partnerComp)
            ? partnerComp
            : anchor;

        // DrawTextureRect writes Modulate directly, which is linear, so this takes the CONVERTED skin colour.
        return Color.FromSrgb(WFCrackCircleOverlay.ColourFor(skin, anchor, partner));
    }

    /// <summary>One tinted line between two world points, or nothing at all if the two have collapsed together.</summary>
    private static void DrawBeam(
        DrawingHandleWorld handle,
        Texture texture,
        Vector2 near,
        Vector2 far,
        float width,
        Color colour)
    {
        var diff = far - near;
        var length = diff.Length();

        if (length <= MinBeamLength)
            return;

        // The texture runs along X, so use the plain atan2 angle; ToWorldAngle would add a quarter turn.
        var midPoint = near + diff / 2f;
        var box = new Box2(-length / 2f, -width / 2f, length / 2f, width / 2f);
        var rotated = new Box2Rotated(box.Translated(midPoint), new Angle(diff), midPoint);

        handle.DrawTextureRect(texture, rotated, colour);
    }
}
