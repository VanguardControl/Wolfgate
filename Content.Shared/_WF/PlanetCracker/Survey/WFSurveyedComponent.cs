using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// What one player has revealed with a surveyor. It lives on the PLAYER, not on the vein, because the cardinality runs
/// that way: one vein is revealed to one player, so a flag on the vein would either leak the vein to every client that
/// already has it in PVS or need Component.SessionSpecific plus per-viewer ComponentGetStateAttemptEvent filtering.
/// Reveals are permanent for the round and survive stowing the surveyor and reconnecting.
/// </summary>
// The `true` on AutoGenerateComponentState is MANDATORY and the client half depends on it: the generator emits the
// `new AfterAutoHandleStateEvent(args.Current)` raise only inside `if (raiseAfterAutoHandle)`
// (RobustToolbox/Robust.Shared.CompNetworkGenerator/ComponentNetworkGenerator.cs:551-560), the attribute's first
// parameter defaults to FALSE (RobustToolbox/Robust.Shared/Analyzers/ComponentNetworkGeneratorAuxiliary.cs:55), and
// AfterAutoHandleStateAnalyzer reports MissingAttributeParam at DiagnosticSeverity.Error when it is not true
// (RobustToolbox/Robust.Analyzers/AfterAutoHandleStateAnalyzer.cs:29-38, :78-83). Without it the client's only reveal
// trigger never fires and the client build breaks on this file.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause, UnsavedComponent]
public sealed partial class WFSurveyedComponent : Component
{
    /// <summary>Every deep vein this player has pulsed, by net entity.</summary>
    [DataField, AutoNetworkedField]
    public HashSet<NetEntity> Revealed = new();

    /// <summary>Where the last pulse went off, for the overlay's expanding fade.</summary>
    [DataField, AutoNetworkedField]
    public NetCoordinates? LastPulse;

    /// <summary>Radius of the last pulse, in tiles.</summary>
    [DataField, AutoNetworkedField]
    public float LastPulseRadius;

    /// <summary>When the last pulse's overlay fade ends.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan PulseFadeEnd;

    /// <summary>How long a pulse's overlay fade lasts.</summary>
    [DataField]
    public TimeSpan PulseFade = TimeSpan.FromSeconds(3);
}
