using System.Linq;
using System.Numerics;
using Content.Client._Mono.Radar;
using Content.Client._WF.CombatConsole;
using Content.Client.Shuttles.UI;
using Content.Shared._Mono.FireControl;
using Content.Shared.Physics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Client._WF.Cockpit;

/// <summary>Draws the gunnery console's weapon-to-aim preview lines over the cockpit NAV plot.</summary>
public sealed class WFCockpitAimLines : Control
{
    [Dependency] private IEntityManager _entities = default!;

    private readonly ShuttleNavControl _navigation;
    private readonly Func<EntityCoordinates?> _aim;
    private readonly Func<NetEntity, bool> _selected;
    private readonly Dictionary<NetEntity, Color> _blipColors = new();
    private FireControllableEntry[] _weapons = Array.Empty<FireControllableEntry>();

    public WFCockpitAimLines(ShuttleNavControl navigation, Func<EntityCoordinates?> aim, Func<NetEntity, bool> selected)
    {
        IoCManager.InjectDependencies(this);
        _navigation = navigation;
        _aim = aim;
        _selected = selected;
        MouseFilter = MouseFilterMode.Ignore;
    }

    /// <summary>Replaces the linked console's weapon list; null clears it.</summary>
    public void UpdateWeapons(FireControllableEntry[]? weapons) => _weapons = weapons ?? Array.Empty<FireControllableEntry>();

    /// <summary>Plot-pixel segments from each selected weapon with a clear line of fire to the aim point.</summary>
    public IEnumerable<(Vector2 From, Vector2 To, Color Color)> Lines()
    {
        if (_weapons.Length == 0 || _aim() is not { } aim || !aim.IsValid(_entities) ||
            !_navigation.WfCockpitTryGetWorldToView(out var worldToView, out var map, out var tracked))
            yield break;

        var transform = _entities.System<SharedTransformSystem>();
        var target = transform.ToMapCoordinates(aim);
        if (target.MapId != map)
            yield break;

        var physics = _entities.System<SharedPhysicsSystem>();
        var targetView = Vector2.Transform(target.Position, worldToView);
        _blipColors.Clear();
        foreach (var blip in _entities.System<RadarBlipsSystem>().GetCurrentBlips())
            _blipColors[blip.NetUid] = blip.Config.Color;

        foreach (var weapon in _weapons)
        {
            if (!_selected(weapon.NetEntity))
                continue;
            var coordinates = _entities.GetCoordinates(weapon.Coordinates);
            if (!coordinates.IsValid(_entities))
                continue;
            var origin = transform.ToMapCoordinates(coordinates).Position;
            var direction = target.Position - origin;
            var length = direction.Length();
            if (length < 0.01f)
                continue;
            if (!weapon.IgnoresLos)
            {
                var ray = new CollisionRay(origin, direction / length, (int) CollisionGroup.Impassable);
                if (physics.IntersectRay(map, ray, length, ignoredEnt: tracked).Any())
                    continue;
            }
            var color = _blipColors.GetValueOrDefault(weapon.NetEntity, WFInstrumentTheme.Accent);
            yield return (Vector2.Transform(origin, worldToView), targetView, color.WithAlpha(0.3f));
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        foreach (var (from, to, color) in Lines())
            handle.DrawLine(from, to, color);
    }
}
