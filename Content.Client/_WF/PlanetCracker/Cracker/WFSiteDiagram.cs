using System.Numerics;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Cracker.BUI;
using Robust.Client.Graphics;
using Robust.Shared.Collections;

namespace Content.Client._WF.PlanetCracker.Cracker;

/// <summary>
/// Plan view of the crack site: the hull and its chunk berth, the anchor pair and the cut circle they carve, the
/// alignment tolerance ring and the offset between the two, and the hull's projectors with a beam line each while
/// firing. Every value comes out of <see cref="WFCrackConsoleState"/>; nothing is re-derived off the berth marker or
/// the projector entities, because neither is inside net.pvs_range of a bridge console on a capital hull.
/// </summary>
public sealed class WFSiteDiagram : WFDiagramControl
{
    /// <summary>Spare room left around the framed geometry.</summary>
    private const float FitMargin = 1.10f;

    /// <summary>Segments per tile of cut radius, clamped to the range below; the overlay's own recipe.</summary>
    private const float SegmentsPerTile = 8f;

    /// <summary>Fewest segments a ring is drawn with.</summary>
    private const int MinSegments = 64;

    /// <summary>Most segments a ring is drawn with.</summary>
    private const int MaxSegments = 256;

    /// <summary>Radius of an anchor mark, in control pixels.</summary>
    private const float AnchorMarkRadius = 3f;

    /// <summary>Radius of a projector mark, in control pixels.</summary>
    private const float ProjectorMarkRadius = 2.5f;

    /// <summary>Alpha of the chord joining the two anchors.</summary>
    private const float ChordAlpha = 0.35f;

    /// <summary>Half-width of the arrow head on the offset marker, in control pixels.</summary>
    private const float ArrowHead = 5f;

    /// <summary>Smallest world span the view will frame, so a degenerate state does not divide by zero.</summary>
    private const float MinWorldSpan = 4f;

    private WFCrackConsoleState? _state;

    /// <summary>Hull outline and the projector marks, which share the hull's schematic frame.</summary>
    private ValueList<Vector2> _hullLines;

    /// <summary>The one hull edge the berth opens through, drawn thicker so the plan view reads its facing.</summary>
    private ValueList<Vector2> _berthEdgeLines;

    /// <summary>The berth rectangle itself.</summary>
    private ValueList<Vector2> _berthLines;

    /// <summary>The chord between the two anchors.</summary>
    private ValueList<Vector2> _chordLines;

    /// <summary>The offset arrow from the berth centre to the cut centre.</summary>
    private ValueList<Vector2> _offsetLines;

    /// <summary>Beam lines from every firing projector to the cut centre.</summary>
    private ValueList<Vector2> _beamLines;

    /// <summary>Scratch for the hull's four projected corners, reused so Draw allocates nothing.</summary>
    private readonly Vector2[] _hullCorners = new Vector2[4];

    private Ring _circleRing;
    private Ring _toleranceRing;

    /// <summary>World-to-pixel scale the current Draw framed with.</summary>
    private float _scale = 1f;

    /// <summary>World point the current Draw put in the middle of the control.</summary>
    private Vector2 _worldCentre;

    /// <summary>Middle of the control, in pixels.</summary>
    private Vector2 _screenCentre;

    /// <summary>The state the diagram draws; null until the console has pushed one.</summary>
    public void SetState(WFCrackConsoleState? state)
    {
        _state = state;
    }

    /// <inheritdoc/>
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        RefreshSkin();

        handle.DrawRect(PixelSizeBox, Geom(Skin.Ink));

        if (_state is not { } state)
            return;

        var box = PixelSizeBox;

        if (box.Width <= 0 || box.Height <= 0)
            return;

        // One framing pass over everything worth seeing, then one projection used by the whole draw.
        var bounds = Frame(state);
        _scale = MathF.Min(box.Width / bounds.Width, box.Height / bounds.Height);
        _worldCentre = bounds.Center;
        _screenCentre = new Vector2(box.Width / 2f, box.Height / 2f);

        _hullLines.Clear();
        _berthEdgeLines.Clear();
        _berthLines.Clear();
        _chordLines.Clear();
        _offsetLines.Clear();
        _beamLines.Clear();

        BuildHull(state);
        BuildBerth(state);
        BuildPair(state);
        BuildOffset(state);

        var ringColour = PairColour(state);
        var alignColour = state.Aligned ? Skin.Good : Skin.Danger;

        if (state.CircleRadius > 0f)
        {
            var segments = Math.Clamp((int)(state.CircleRadius * SegmentsPerTile), MinSegments, MaxSegments);
            EnsureRing(ref _circleRing, Project(state.CircleCentre), state.CircleRadius * _scale, segments);
            DrawRing(handle, ref _circleRing, ringColour);
        }

        if (state.AlignTolerance > 0f)
        {
            EnsureRing(ref _toleranceRing, Project(state.BerthCentre), state.AlignTolerance * _scale, MinSegments);
            DrawRing(handle, ref _toleranceRing, alignColour);
        }

        Flush(handle, ref _hullLines, Skin.EdgeLight);
        Flush(handle, ref _berthEdgeLines, Skin.Accent);
        Flush(handle, ref _berthLines, Skin.AccentDim);
        Flush(handle, ref _beamLines, Skin.Caution);
        Flush(handle, ref _chordLines, ringColour.WithAlpha(ChordAlpha));
        Flush(handle, ref _offsetLines, alignColour);

        DrawMarks(handle, state);
    }

    /// <summary>World XY to a point on the control, using the framing the current Draw computed.</summary>
    private Vector2 Project(Vector2 world)
    {
        var local = (world - _worldCentre) * _scale;

        // Screen Y grows downward, world Y up.
        return _screenCentre + new Vector2(local.X, -local.Y);
    }

    /// <summary>World XY of a point given in the hull grid's own coordinates.</summary>
    private static Vector2 HullWorld(WFCrackConsoleState state, Vector2 local)
    {
        return state.HullPos + state.HullRotation.RotateVec(local);
    }

    /// <summary>World bounds the view frames: the hull, the berth, the tolerance ring, the cut circle and the anchors.</summary>
    private static Box2 Frame(WFCrackConsoleState state)
    {
        var half = state.BerthHalfExtents.Length();
        var bounds = Box2.CenteredAround(state.BerthCentre, new Vector2(half * 2f, half * 2f));

        // The hull sits clear of the berth rather than inside it, so framing one without the other cuts it off.
        if (state.HullAabb.Width > 0f && state.HullAabb.Height > 0f)
        {
            bounds = bounds.Union(
                new Box2Rotated(state.HullAabb.Translated(state.HullPos), state.HullRotation, state.HullPos)
                    .CalcBoundingBox());
        }

        if (state.AlignTolerance > 0f)
        {
            bounds = bounds.Union(Box2.CenteredAround(state.BerthCentre,
                new Vector2(state.AlignTolerance * 2f, state.AlignTolerance * 2f)));
        }

        if (state.CircleRadius > 0f)
        {
            bounds = bounds.Union(Box2.CenteredAround(state.CircleCentre,
                new Vector2(state.CircleRadius * 2f, state.CircleRadius * 2f)));
        }

        if (state.AnchorA is not null)
            bounds = bounds.Union(Box2.CenteredAround(state.AnchorAPos, Vector2.One));

        if (state.AnchorB is not null)
            bounds = bounds.Union(Box2.CenteredAround(state.AnchorBPos, Vector2.One));

        var size = Vector2.Max(bounds.Size * FitMargin, new Vector2(MinWorldSpan, MinWorldSpan));
        return Box2.CenteredAround(bounds.Center, size);
    }

    /// <summary>
    /// The hull outline and its projectors, on the hull's own world pose. The berth sits clear of the hull rather than
    /// around it, so drawing the outline on the berth centre would show the two concentric and misstate the one
    /// relationship the plan view exists for. The edge facing the berth is drawn thicker so the facing reads.
    /// </summary>
    private void BuildHull(WFCrackConsoleState state)
    {
        var aabb = state.HullAabb;

        if (aabb.Width <= 0f || aabb.Height <= 0f)
            return;

        Vector2 Hull(Vector2 local)
        {
            return Project(HullWorld(state, local));
        }

        // A reused array, not a collection expression and not a stackalloc: `[a, b, c, d]` lowers to an InlineArray4
        // helper with a byref span accessor, and localloc is unverifiable, so both fail the client sandbox (SandboxTest).
        var corners = _hullCorners;
        corners[0] = Hull(aabb.BottomLeft);
        corners[1] = Hull(aabb.BottomRight);
        corners[2] = Hull(aabb.TopRight);
        corners[3] = Hull(aabb.TopLeft);

        // Project is a uniform scale plus a flip, so the nearest edge in control pixels is the nearest edge in world.
        var berth = Project(state.BerthCentre);
        var centre = Hull(aabb.Center);
        var nearest = 0;
        var nearestDistance = float.MaxValue;

        for (var i = 0; i < corners.Length; i++)
        {
            var mid = (corners[i] + corners[(i + 1) % corners.Length]) / 2f;
            var distance = (mid - berth).LengthSquared();

            if (distance >= nearestDistance)
                continue;

            nearest = i;
            nearestDistance = distance;
        }

        for (var i = 0; i < corners.Length; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Length];

            if (i != nearest)
            {
                AddLine(ref _hullLines, a, b);
                continue;
            }

            // DrawPrimitives lines are one pixel wide with no width parameter, so "thicker" is the same edge laid down
            // twice, a pixel further into the hull.
            var inward = centre - (a + b) / 2f;
            inward = inward.LengthSquared() > float.Epsilon ? inward / inward.Length() : Vector2.Zero;

            AddLine(ref _berthEdgeLines, a, b);
            AddLine(ref _berthEdgeLines, a + inward, b + inward);
        }

        var firing = state.State == WFCrackState.Cracking && !state.CrackPaused;

        foreach (var row in state.Projectors)
        {
            if (!firing || row.State != WFProjectorState.Firing)
                continue;

            AddLine(ref _beamLines, Hull(row.GridLocalPos), Project(state.CircleCentre));
        }
    }

    /// <summary>The berth rectangle, from its centre, half-extents and world rotation.</summary>
    private void BuildBerth(WFCrackConsoleState state)
    {
        var half = state.BerthHalfExtents;

        if (half.X <= 0f || half.Y <= 0f)
            return;

        var rotation = state.BerthRotation;
        var centre = state.BerthCentre;

        Vector2 Corner(float x, float y)
        {
            return Project(centre + rotation.RotateVec(new Vector2(half.X * x, half.Y * y)));
        }

        AddQuad(ref _berthLines, Corner(-1f, -1f), Corner(1f, -1f), Corner(1f, 1f), Corner(-1f, 1f));
    }

    /// <summary>The chord between the two shown anchors.</summary>
    private void BuildPair(WFCrackConsoleState state)
    {
        if (!HasPair(state))
            return;

        AddLine(ref _chordLines, Project(state.AnchorAPos), Project(state.AnchorBPos));
    }

    /// <summary>The offset arrow from the berth centre to the cut centre, with a head at the cut end.</summary>
    private void BuildOffset(WFCrackConsoleState state)
    {
        if (state.CircleRadius <= 0f)
            return;

        var from = Project(state.BerthCentre);
        var to = Project(state.CircleCentre);
        var delta = to - from;

        if (delta.LengthSquared() <= float.Epsilon)
            return;

        AddLine(ref _offsetLines, from, to);

        var dir = delta / delta.Length();
        var side = new Vector2(-dir.Y, dir.X);

        AddLine(ref _offsetLines, to, to - dir * ArrowHead * 2f + side * ArrowHead);
        AddLine(ref _offsetLines, to, to - dir * ArrowHead * 2f - side * ArrowHead);
    }

    /// <summary>
    /// The filled marks: the two anchors and every projector. Filled DrawCircle takes the RAW skin colour.
    /// </summary>
    private void DrawMarks(DrawingHandleScreen handle, WFCrackConsoleState state)
    {
        var aabb = state.HullAabb;

        if (aabb.Width > 0f && aabb.Height > 0f)
        {
            foreach (var row in state.Projectors)
            {
                handle.DrawCircle(Project(HullWorld(state, row.GridLocalPos)), ProjectorMarkRadius,
                    ProjectorColour(row), true);
            }
        }

        if (!HasPair(state))
            return;

        handle.DrawCircle(Project(state.AnchorAPos), AnchorMarkRadius,
            AnchorColour(state.AnchorAState, state.AnchorADamaged), true);

        handle.DrawCircle(Project(state.AnchorBPos), AnchorMarkRadius,
            AnchorColour(state.AnchorBState, state.AnchorBDamaged), true);
    }

    /// <summary>True when the state holds a pair worth drawing, targeted or merely a candidate.</summary>
    private static bool HasPair(WFCrackConsoleState state)
    {
        return (state.AnchorA ?? state.CandidateA) is not null && (state.AnchorB ?? state.CandidateB) is not null;
    }

    /// <summary>
    /// Ring colour from the worse of the two halves, matching WFCrackCircleOverlay's rule exactly so the diagram and
    /// the world overlay never disagree about what the crew is looking at.
    /// </summary>
    private Color PairColour(WFCrackConsoleState state)
    {
        if (state.AnchorAState == WFAnchorState.Broken || state.AnchorBState == WFAnchorState.Broken ||
            state.AnchorADamaged || state.AnchorBDamaged)
        {
            return Skin.Danger;
        }

        if (state.AnchorAState is WFAnchorState.Locked or WFAnchorState.Off &&
            state.AnchorBState is WFAnchorState.Locked or WFAnchorState.Off)
        {
            return Skin.Good;
        }

        if (state.AnchorAState == WFAnchorState.Drilling || state.AnchorBState == WFAnchorState.Drilling)
            return Skin.Caution;

        return Skin.AccentDim;
    }

    /// <summary>One anchor's mark colour, on the same rule as the ring.</summary>
    private Color AnchorColour(WFAnchorState anchorState, bool damaged)
    {
        if (anchorState == WFAnchorState.Broken || damaged)
            return Skin.Danger;

        return anchorState switch
        {
            WFAnchorState.Locked or WFAnchorState.Off => Skin.Good,
            WFAnchorState.Drilling => Skin.Caution,
            _ => Skin.AccentDim,
        };
    }

    /// <summary>One projector's mark colour.</summary>
    private Color ProjectorColour(WFProjectorRow row)
    {
        if (row.Broken)
            return Skin.Danger;

        if (!row.Powered)
            return Skin.Caution;

        return row.State switch
        {
            WFProjectorState.Firing => Skin.Accent,
            WFProjectorState.Charging => Skin.Caution,
            _ => Skin.AccentDim,
        };
    }
}
