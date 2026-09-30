using System.Numerics;
using Content.Shared._WF.ShipShields;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.ShipShields;

/// <summary>Draws cached hull fields with hex cells and bounded impact-color sampling.</summary>
public sealed class WFShipShieldOverlay : Overlay
{
    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transforms;
    private readonly IGameTiming _timing;
    private readonly Dictionary<EntityUid, List<WFShipShieldOverlaySystem.Impact>> _impacts;
    private readonly Dictionary<EntityUid, CachedField> _fields = new();
    private readonly List<EntityUid> _expired = new();
    private readonly ShaderInstance _shader;
    private readonly ShaderInstance _distantShader;

    /// <summary>Draws unlit energy beneath ship sprites.</summary>
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    /// <summary>Binds the impact cache and reusable field meshes.</summary>
    public WFShipShieldOverlay(IEntityManager entities, IPrototypeManager prototypes, IGameTiming timing,
        Dictionary<EntityUid, List<WFShipShieldOverlaySystem.Impact>> impacts)
    {
        _entities = entities;
        _transforms = entities.System<SharedTransformSystem>();
        _timing = timing;
        _impacts = impacts;
        _shader = prototypes.Index<ShaderPrototype>("WFShipShieldShimmer").Instance();
        _distantShader = prototypes.Index<ShaderPrototype>("unshaded").Instance();
        ZIndex = 8;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var pixelsPerTile = args.ViewportBounds.Width / MathF.Max(1f, args.WorldAABB.Width);
        var distant = pixelsPerTile < 10f;
        _expired.Clear();
        foreach (var uid in _fields.Keys)
        {
            if (_entities.Deleted(uid))
                _expired.Add(uid);
        }
        foreach (var uid in _expired)
            _fields.Remove(uid);
        handle.UseShader(distant ? _distantShader : _shader);
        var query = _entities.EntityQueryEnumerator<WFShipShieldVisualsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var shield, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;
            if (!_fields.TryGetValue(uid, out var cached) || !WFShipShieldMesh.ContoursEqual(cached.Contours, shield.Contours))
            {
                cached = new CachedField(shield.Contours);
                _fields[uid] = cached;
            }
            cached.Contours = shield.Contours;
            var anchor = xform;
            if (shield.Grid is { } grid && _entities.TryGetComponent<TransformComponent>(grid, out var gridTransform))
                anchor = gridTransform;
            var matrix = _transforms.GetWorldMatrix(anchor);
            var bounds = cached.Bounds;
            var a = Vector2.Transform(bounds.BottomLeft, matrix);
            var b = Vector2.Transform(bounds.TopRight, matrix);
            var c = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), matrix);
            var d = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), matrix);
            var worldBounds = new Box2(Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d)),
                Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d)));
            if (!args.WorldAABB.Intersects(worldBounds))
                continue;
            var mesh = distant ? cached.Distant ??= new RenderMesh(cached.Contours, true)
                : cached.Detailed ??= new RenderMesh(cached.Contours, false);
            _impacts.TryGetValue(uid, out var impacts);
            var tint = WFShipShieldEffects.HealthColor(shield.Health);
            mesh.UpdateColors(tint, impacts, _timing.CurTime, distant);
            handle.SetTransform(matrix);
            DrawBatches(handle, DrawPrimitiveTopology.TriangleList, mesh.Triangles);
            if (!distant && pixelsPerTile >= 12f)
                DrawBatches(handle, DrawPrimitiveTopology.LineList, mesh.HexLines);
        }
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    private static void DrawBatches(DrawingHandleWorld handle, DrawPrimitiveTopology topology, DrawVertexUV2DColor[] vertices)
    {
        for (var offset = 0; offset < vertices.Length; offset += 8190)
            handle.DrawPrimitives(topology, Texture.White,
                new ReadOnlySpan<DrawVertexUV2DColor>(vertices, offset, Math.Min(8190, vertices.Length - offset)));
    }

    private sealed class CachedField
    {
        public Vector2[][] Contours;
        public readonly Box2 Bounds;
        public RenderMesh? Detailed;
        public RenderMesh? Distant;

        public CachedField(Vector2[][] contours)
        {
            Contours = contours;
            var initialized = false;
            var bounds = new Box2();
            foreach (var contour in contours)
            foreach (var point in contour)
            {
                if (!initialized)
                {
                    bounds = new Box2(point, point);
                    initialized = true;
                }
                bounds = bounds.ExtendToContain(point);
            }
            Bounds = new Box2(bounds.BottomLeft - new Vector2(WFShipShieldMesh.InwardDepth),
                bounds.TopRight + new Vector2(WFShipShieldMesh.InwardDepth));
        }
    }

    private sealed class RenderMesh
    {
        private readonly WFShipShieldMesh _mesh;
        private readonly Color[] _colors;
        public readonly DrawVertexUV2DColor[] Triangles;
        public readonly DrawVertexUV2DColor[] HexLines;
        private Color? _lastTint;
        private bool _hadImpacts;
        private TimeSpan _nextUpdate;

        public RenderMesh(Vector2[][] contours, bool distant)
        {
            _mesh = new WFShipShieldMesh(contours, distant);
            _colors = new Color[_mesh.Samples.Count];
            Triangles = new DrawVertexUV2DColor[_mesh.Triangles.Count];
            HexLines = new DrawVertexUV2DColor[_mesh.HexLines.Count];
        }

        public void UpdateColors(Color tint, List<WFShipShieldOverlaySystem.Impact>? impacts, TimeSpan time, bool distant)
        {
            var active = impacts is { Count: > 0 };
            if (!active && !_hadImpacts && _lastTint == tint)
                return;
            if (active && _hadImpacts && _lastTint == tint && time < _nextUpdate)
                return;
            _nextUpdate = time + TimeSpan.FromSeconds(distant ? 1f / 15f : 1f / 30f);
            _lastTint = tint;
            _hadImpacts = active;
            for (var i = 0; i < _colors.Length; i++)
            {
                var heat = 0f;
                var wave = 0f;
                if (active && impacts != null)
                {
                    for (var hit = Math.Max(0, impacts.Count - (distant ? 8 : 32)); hit < impacts.Count; hit++)
                    {
                        var impact = impacts[hit];
                        var age = (float) (time - impact.Time).TotalSeconds;
                        var distance = Vector2.Distance(_mesh.Samples[i], impact.Position);
                        heat += WFShipShieldEffects.Heat(distance, age, impact.Strength);
                        wave += WFShipShieldEffects.Wave(distance, age, impact.Strength);
                    }
                }
                var color = Color.InterpolateBetween(tint, new Color(1f, 0.025f, 0.06f), Math.Clamp(heat, 0f, 1f));
                color = Color.InterpolateBetween(color, new Color(0.7f, 0.92f, 1f), Math.Clamp(wave * 0.65f, 0f, 0.8f));
                _colors[i] = Color.FromSrgb(color).WithAlpha(1f + wave * 2.5f + heat * 0.6f);
            }
            Fill(_mesh.Triangles, Triangles);
            Fill(_mesh.HexLines, HexLines);
        }

        private void Fill(List<WFShipShieldMesh.Vertex> source, DrawVertexUV2DColor[] destination)
        {
            for (var i = 0; i < source.Count; i++)
            {
                var vertex = source[i];
                var color = _colors[vertex.Sample];
                destination[i] = new DrawVertexUV2DColor(vertex.Position, vertex.Position,
                    color.WithAlpha(Math.Clamp(color.A * vertex.Alpha, 0f, 0.85f)));
            }
        }
    }
}
