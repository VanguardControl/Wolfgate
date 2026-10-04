using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Encounters;

/// <summary>Where every visible encounter is, sent to all players for the sector markers on their radars.</summary>
[Serializable, NetSerializable]
public sealed class WFEncounterMarkersEvent : EntityEventArgs
{
    public List<WFEncounterMarker> Markers = new();
}

[Serializable, NetSerializable]
public sealed class WFEncounterMarker
{
    public string Name = string.Empty;
    public WFEncounterCategory Category;
    public MapId Map;
    public Vector2 Position;
    public Vector2 Velocity;

    /// <summary>Radius inside which ships are warned off; zero for none.</summary>
    public float WarnRange;

    /// <summary>Radius inside which ships are fired on; zero for none.</summary>
    public float AttackRange;
}
