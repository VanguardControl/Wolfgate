using System.Text.Json;
using Content.Server.Database;
using Content.Shared._WG.LogInvestigator;
using Content.Shared.Database;
using Robust.Shared.Player;

namespace Content.Server.Administration.Logs;

/// <summary>Writes where the logged entities were into the JSON of the log types that need it.</summary>
public sealed partial class AdminLogManager
{
    private readonly HashSet<LogType> _wgPositionTypes = new();
    private SharedTransformSystem? _wgTransform;
    private bool _wgPositionsWatched;

    /// <summary>The log's JSON, with "wgPos" beside its entities when the type records positions.</summary>
    private (JsonDocument Json, HashSet<Guid> Players) WGToJson(LogType type, Dictionary<string, object?> properties)
    {
        if (!_wgPositionsWatched)
        {
            _wgPositionsWatched = true;
            _configuration.OnValueChanged(LogInvestigatorCVars.PositionTypes, WGPositionTypesChanged, true);
        }

        if (!_wgPositionTypes.Contains(type))
            return ToJson(properties);

        Dictionary<string, object>? positions = null;
        foreach (var (key, value) in properties)
        {
            var uid = value switch
            {
                EntityUid id => id,
                EntityStringRepresentation rep => rep.Uid,
                ICommonSession { AttachedEntity: { } attached } => attached,
                IComponent component => component.Owner,
                _ => (EntityUid?) null,
            };

            if (uid == null || !_entityManager.TryGetComponent(uid, out TransformComponent? xform) || xform.MapID == default)
                continue;

            _wgTransform ??= _entityManager.System<SharedTransformSystem>();
            var world = _wgTransform.GetWorldPosition(xform);
            positions ??= new Dictionary<string, object>();
            positions[NamingPolicy.ConvertName(key)] = new WGLogPosition(
                (int) xform.MapID,
                xform.GridUid is { } grid ? (int?) (int) grid : null,
                MathF.Round(world.X, 1),
                MathF.Round(world.Y, 1));
        }

        if (positions == null)
            return ToJson(properties);

        var withPositions = new Dictionary<string, object?>(properties) { [ServerDbBase.AdminLogPositionKey] = positions };
        return ToJson(withPositions);
    }

    private void WGPositionTypesChanged(string value)
    {
        _wgPositionTypes.Clear();
        foreach (var name in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse<LogType>(name, out var type))
                _wgPositionTypes.Add(type);
            else
                _sawmill.Warning($"{LogInvestigatorCVars.PositionTypes.Name} names an unknown log type: {name}");
        }
    }

    /// <summary>Where an entity was: its map, grid (entity id, as logs write ids) and world position.</summary>
    private sealed record WGLogPosition(int Map, int? Grid, float X, float Y);
}
