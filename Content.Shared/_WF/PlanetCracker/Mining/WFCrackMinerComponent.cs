using Content.Shared.Mining;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.PlanetCracker.Mining;

/// <summary>A machine that turns the deep vein anchored under its own tile into ore stacks while its cell holds out. F6.</summary>
/// <remarks>
/// (a) Deliberately NOT [NetworkedComponent]: nothing on the client reads this component - the sprite is Appearance
/// + GenericVisualizer and examine is server-side - so networking it would be dead weight. WFGravityAnchorComponent
/// networks its State only because the client's crack-circle overlay reads it.
/// (b) AutoGenerateComponentPause only repairs NextTick across a pause - the generator emits nothing but an
/// EntityUnpausedEvent handler that adds args.PausedTime (RobustToolbox/Robust.Serialization.Generator/ComponentPauseGenerator.cs:160-200)
/// - so it is WFCrackMinerSystem.Update's own Paused(uid) check that actually stops a paused miner working.
/// </remarks>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WFCrackMinerComponent : Component
{
    /// <summary>Current state; the appearance key and the ambience are written from it.</summary>
    [DataField]
    public WFCrackMinerState State = WFCrackMinerState.Idle;

    /// <summary>How often the miner draws charge and cuts rock.</summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>Next production tick; carried across a map pause so an unpaused chunk does not owe a burst of ticks.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextTick;

    /// <summary>Joules taken from the cell per Interval. 1 J/s puts a PowerCellHigh (1080 J) at 18 minutes, roughly one average vein.</summary>
    [DataField]
    public float DrawRate = 1f;

    /// <summary>Ore units buffered before a stack is dropped; 25 at 150/min is one drop every ten seconds and one entity per drop.</summary>
    [DataField]
    public int BatchSize = 25;

    /// <summary>Random scatter radius for the dropped stack, so a pile does not stack on one pixel.</summary>
    [DataField]
    public float OutputSpread = 0.35f;

    /// <summary>Fractional ore carried between ticks, so any Rate lands exactly over a minute.</summary>
    [DataField]
    public float Carry;

    /// <summary>Whole ore units cut but not yet dropped.</summary>
    [DataField]
    public int Buffer;

    /// <summary>What the buffer is made of, so a flush after the miner leaves its vein still knows what to spawn.</summary>
    [DataField]
    public ProtoId<OrePrototype>? BufferedOre;
}
