using System.Numerics;
using Content.Client._WF.Shuttles.UI;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Doors.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._WF.ShipAccess;

/// <summary>
/// The access tab's door diagram: the hull from the ship view, with every door the client can see drawn
/// as a node in its rule's colour. Clicking near a node selects it.
/// </summary>
public sealed class ShipAccessDoorMapControl : ShipViewControl
{
    /// <summary>How close to a node, in pixels, a click has to land.</summary>
    private const float SelectRadius = 8f;

    /// <summary>Node colours by rule, in enum order.</summary>
    private static readonly Color[] RuleColors =
    {
        Color.FromHex("#8fb58f"),
        Color.FromHex("#e8c04a"),
        Color.FromHex("#3fa0ff"),
        Color.FromHex("#b86bff"),
        Color.FromHex("#3fd6c8"),
        Color.FromHex("#40ff40"),
        Color.FromHex("#ff3030"),
    };

    private readonly List<ShipAccessDoorNode> _doors = new();
    private EntityUid? _grid;

    /// <summary>Doors on the grid the client knows about, top row first. Doors outside the client's view are missing.</summary>
    public IReadOnlyList<ShipAccessDoorNode> Doors => _doors;

    /// <summary>The door drawn with a ring, or null.</summary>
    public EntityUid? Selected { get; set; }

    /// <summary>The viewer clicked a door node.</summary>
    public event Action<EntityUid>? DoorSelected;

    public ShipAccessDoorMapControl()
    {
        PostWallDrawingAction += DrawDoors;
    }

    /// <summary>The colour a rule is drawn in, on the diagram, the legend and the list.</summary>
    public static Color ColorFor(WFDoorAccessRule rule)
    {
        var i = (int) rule;
        return i >= 0 && i < RuleColors.Length ? RuleColors[i] : RuleColors[0];
    }

    public override void SetGrid(EntityUid? grid)
    {
        _grid = grid;
        Selected = null;
        _doors.Clear();
        base.SetGrid(grid);
    }

    /// <summary>Re-reads the doors on the grid. The screen calls this on its own refresh tick.</summary>
    public void RefreshDoors()
    {
        _doors.Clear();
        if (_grid == null)
            return;

        var query = EntManager.EntityQueryEnumerator<DoorComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            // The node sits at the door's grid-local position, so only doors parented straight to the grid count.
            if (xform.ParentUid != _grid)
                continue;

            var rule = EntManager.TryGetComponent<WFDoorAccessRuleComponent>(uid, out var comp) ? comp.Rule : WFDoorAccessRule.Default;
            _doors.Add(new ShipAccessDoorNode(uid, meta.EntityName, xform.LocalPosition, rule, comp?.HasOwnCode ?? false));
        }

        _doors.Sort((a, b) =>
        {
            var y = b.Position.Y.CompareTo(a.Position.Y);
            return y != 0 ? y : a.Position.X.CompareTo(b.Position.X);
        });

        if (Selected != null && !Contains(Selected.Value))
            Selected = null;
    }

    private bool Contains(EntityUid uid)
    {
        foreach (var door in _doors)
        {
            if (door.Uid == uid)
                return true;
        }

        return false;
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick || _doors.Count == 0)
            return;

        // A drag pans the map; only a click picks.
        if ((StartDragPosition - args.PointerLocation.Position).Length() > MinDragDistance)
            return;

        var local = args.PointerLocation.Position - GlobalPixelPosition;
        var unscaled = (local - MidPointVector) / MinimapScale;
        var gridPos = new Vector2(unscaled.X, -unscaled.Y) + GetOffset();

        ShipAccessDoorNode? closest = null;
        var closestDistance = float.PositiveInfinity;
        foreach (var door in _doors)
        {
            var distance = (door.Position - gridPos).Length() * MinimapScale;
            if (distance > SelectRadius || distance >= closestDistance)
                continue;

            closest = door;
            closestDistance = distance;
        }

        if (closest == null)
            return;

        Selected = closest.Uid;
        DoorSelected?.Invoke(closest.Uid);
    }

    private void DrawDoors(DrawingHandleScreen handle)
    {
        if (_doors.Count == 0)
            return;

        var offset = GetOffset();
        var radius = MathF.Max(3f, MinimapScale * 0.35f);

        foreach (var door in _doors)
        {
            var p = door.Position - offset;
            var pos = ScalePosition(new Vector2(p.X, -p.Y));
            if (door.Uid == Selected)
                handle.DrawCircle(pos, radius + 3f, Color.White);

            handle.DrawCircle(pos, radius, ColorFor(door.Rule));
        }
    }
}

/// <summary>One door as the diagram last saw it.</summary>
public sealed record ShipAccessDoorNode(EntityUid Uid, string Name, Vector2 Position, WFDoorAccessRule Rule, bool HasOwnCode);
