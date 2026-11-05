using System.Numerics;
using Content.Shared._WF.Lattice;
using Robust.Client.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// The local player's view through the Lattice field: names and numbers garble in a garbling band, by the deeper of
/// where the viewer is and where the thing seen is, and flicker as they garble.
/// </summary>
public sealed partial class WFLatticeViewSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Seconds between changes in how a garbled text reads.</summary>
    private const double FlickerSeconds = 0.6;

    private readonly List<(Vector2 Position, float Strength)> _anchors = new();

    /// <summary>Garbles text seen by the local player; unchanged in anchored space.</summary>
    /// <param name="map">The map the text belongs to.</param>
    /// <param name="seen">Where the thing named is, if it has a place.</param>
    /// <param name="seed">Keeps each text's garbling its own.</param>
    /// <param name="scale">Share of the usual garbling, for text that should hold up better.</param>
    public string Garble(string text, EntityUid? map, Vector2? seen, int seed, float scale = 1f)
    {
        if (map is not { } mapUid
            || !TryComp<WFLatticeFieldComponent>(mapUid, out var field)
            || !_proto.TryIndex(field.Profile, out var profile))
            return text;

        var depth = -1f;
        if (_player.LocalEntity is { } viewer && Transform(viewer).MapUid == mapUid)
            depth = Depth(field, profile, _transform.GetWorldPosition(viewer));

        if (seen is { } position)
            depth = MathF.Max(depth, Depth(field, profile, position));

        if (depth < 0f)
            return text;

        var tick = (int) (_timing.RealTime.TotalSeconds / FlickerSeconds);
        return WFLatticeMath.Garble(text, WFLatticeMath.GarbleStrength(depth) * scale, seed * 7919 + tick);
    }

    /// <summary>
    /// How wrong Kyphrus looks to the local player, 0 to 1: the share of primary anchors lost, or more out in a garbling
    /// band, where it always looks wrong.
    /// </summary>
    public float Unmooring()
    {
        if (_player.LocalEntity is not { } viewer
            || Transform(viewer).MapUid is not { } map
            || !TryComp<WFLatticeFieldComponent>(map, out var field)
            || !_proto.TryIndex(field.Profile, out var profile))
            return 0f;

        var lost = 1f - field.Integrity;
        var depth = Depth(field, profile, _transform.GetWorldPosition(viewer));
        return Math.Clamp(depth >= 0f ? MathF.Max(lost, 0.6f + 0.4f * depth) : lost, 0f, 1f);
    }

    /// <summary>Garbles text about where the local player is, such as their own coordinates.</summary>
    public string GarbleForViewer(string text, int seed, float scale = 1f)
    {
        return _player.LocalEntity is { } viewer ? Garble(text, Transform(viewer).MapUid, null, seed, scale) : text;
    }

    /// <summary>Depth into the band at a point, or -1 where the band does not garble.</summary>
    private float Depth(WFLatticeFieldComponent field, WFLatticeProfilePrototype profile, Vector2 position)
    {
        _anchors.Clear();
        for (var i = 0; i < field.Anchors.Count && i < field.Strengths.Count; i++)
        {
            _anchors.Add((field.Anchors[i], field.Strengths[i]));
        }

        var strength = WFLatticeMath.Field(position, _anchors, profile.Reach);
        var band = WFLatticeMath.Band(strength, field.Thresholds);
        if (band >= profile.Bands.Count || !profile.Bands[band].Garbled)
            return -1f;

        return WFLatticeMath.Depth(strength, band, field.Thresholds);
    }
}
