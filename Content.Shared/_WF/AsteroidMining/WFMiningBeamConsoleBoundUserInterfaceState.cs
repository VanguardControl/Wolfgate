using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.AsteroidMining;

[Serializable, NetSerializable]
public enum WFMiningBeamUiKey : byte
{
    Key,
}

/// <summary>Sets an emitter's power from zero to one.</summary>
[Serializable, NetSerializable]
public sealed class WFMiningBeamPowerMessage(NetEntity emitter, float power) : BoundUserInterfaceMessage
{
    public readonly NetEntity Emitter = emitter;
    public readonly float Power = power;
}

[Serializable, NetSerializable]
public sealed class WFMiningBeamConsoleBoundUserInterfaceState(bool connected, float range,
    WFMiningBeamEmitterEntry[] emitters, WFMiningBeamTargetEntry[] targets) : BoundUserInterfaceState
{
    public readonly bool Connected = connected;
    public readonly float Range = range;
    public readonly WFMiningBeamEmitterEntry[] Emitters = emitters;
    public readonly WFMiningBeamTargetEntry[] Targets = targets;
}

[Serializable, NetSerializable]
public readonly record struct WFMiningBeamEmitterEntry(
    NetEntity Entity,
    string Name,
    NetEntity? Target,
    bool Active,
    float Power,
    float Strength,
    float MaxRange,
    float Cooldown,
    // Gun position relative to the console, in the console grid's orientation.
    Vector2 Position,
    // Barrel direction in the console grid's orientation.
    Vector2 Direction);

[Serializable, NetSerializable]
public readonly record struct WFMiningBeamOreEntry(string Name, float Fraction, Color Color);

[Serializable, NetSerializable]
public readonly record struct WFMiningBeamTargetEntry(
    NetEntity Entity,
    string Name,
    // Centre relative to the console's grid, in grid coordinates.
    Vector2 Position,
    float Radius,
    float Mass,
    WFMiningBeamOreEntry[] Ores,
    float Resistance,
    float Instability,
    float WindowLow,
    float WindowHigh,
    float Charge,
    float Progress,
    // Overcharge time as a share of the time it takes to blow up.
    float Overcharge,
    // Heat all beams on the rock deliver, before resistance.
    float IncomingPower,
    Color Color);
