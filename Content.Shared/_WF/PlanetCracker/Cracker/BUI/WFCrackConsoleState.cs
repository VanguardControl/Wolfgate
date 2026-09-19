using System.Numerics;
using Content.Shared._WF.PlanetCracker.Anchors;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Cracker.BUI;

/// <summary>
/// Everything the crack console window draws, authored server-side.
/// Grid membership is not a PVS exemption (D-N): the berth marker sits 26 tiles past itself on a MarkerBase prototype
/// and berth-side projectors on a capital hull are routinely past net.pvs_range, so nothing here may be re-derived
/// client-side off those entities. Spin/AtFull/Load/Capacity are also on WFCentrifugeComponent on purpose (D-D2) -
/// that channel feeds the machine's own window, whose viewer is standing at the machine; this one feeds a console
/// that may be nowhere near it. Both are written by the same system in the same place.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFCrackConsoleState : BoundUserInterfaceState
{
    /// <summary>Stage of the hull's crack.</summary>
    public WFCrackState State;

    /// <summary>The cracker hull this console belongs to, or null when the console is not on one.</summary>
    public NetEntity? Cracker;

    /// <summary>First half of the targeted pair.</summary>
    public NetEntity? AnchorA;

    /// <summary>Second half of the targeted pair.</summary>
    public NetEntity? AnchorB;

    /// <summary>First half of the owned pair that could be targeted.</summary>
    public NetEntity? CandidateA;

    /// <summary>Second half of the owned pair that could be targeted.</summary>
    public NetEntity? CandidateB;

    /// <summary>World XY of the first anchor of the shown pair.</summary>
    public Vector2 AnchorAPos;

    /// <summary>World XY of the second anchor of the shown pair.</summary>
    public Vector2 AnchorBPos;

    /// <summary>Lifecycle state of the first anchor.</summary>
    public WFAnchorState AnchorAState;

    /// <summary>Lifecycle state of the second anchor.</summary>
    public WFAnchorState AnchorBState;

    /// <summary>True when the first anchor is past its damage threshold and pausing the crack.</summary>
    public bool AnchorADamaged;

    /// <summary>True when the second anchor is past its damage threshold and pausing the crack.</summary>
    public bool AnchorBDamaged;

    /// <summary>World XY centre of the hull's chunk berth.</summary>
    public Vector2 BerthCentre;

    /// <summary>Half-extents of the berth rectangle, in tiles.</summary>
    public Vector2 BerthHalfExtents;

    /// <summary>World rotation of the berth rectangle.</summary>
    public Angle BerthRotation;

    /// <summary>Grid-local bounds of the hull, for the site diagram's outline.</summary>
    public Box2 HullAabb;

    /// <summary>World XY of the hull grid's origin; <see cref="HullAabb"/> is grid-local around this point.</summary>
    public Vector2 HullPos;

    /// <summary>World rotation of the hull grid, so the outline is placed rather than guessed.</summary>
    public Angle HullRotation;

    /// <summary>World XY centre of the cut circle, on the ground map.</summary>
    public Vector2 CircleCentre;

    /// <summary>Cut radius in tiles, per design D21.</summary>
    public float CircleRadius;

    /// <summary>Raw XY delta from the berth centre to the circle centre; the two sit on different maps (D-H).</summary>
    public Vector2 BerthOffset;

    /// <summary>Tiles of offset the targeting precondition allows.</summary>
    public float AlignTolerance;

    /// <summary>True when BerthOffset is inside AlignTolerance.</summary>
    public bool Aligned;

    /// <summary>Time left on the crack.</summary>
    public TimeSpan CrackRemaining;

    /// <summary>Full duration the crack was begun with, for the progress bar.</summary>
    public TimeSpan CrackTotal;

    /// <summary>True while a damaged anchor is holding the crack.</summary>
    public bool CrackPaused;

    /// <summary>Time left before the hull drops.</summary>
    public TimeSpan GraceRemaining;

    /// <summary>True while the grace countdown is running.</summary>
    public bool GraceRunning;

    /// <summary>True while the disconnect pairing window is open and waiting on the second anchor.</summary>
    public bool DisconnectArmed;

    /// <summary>Time left before the disconnect window lapses and the first anchor re-arms.</summary>
    public TimeSpan DisconnectRemaining;

    /// <summary>True while the evacuation countdown is running.</summary>
    public bool EvacRunning;

    /// <summary>Time left before the chunk is released.</summary>
    public TimeSpan EvacRemaining;

    /// <summary>Which preconditions are failing; the window names them from locale keys, never a server string.</summary>
    public WFCrackFailure Failing;

    /// <summary>Time left on the abort spin-down.</summary>
    public TimeSpan AbortRemaining;

    /// <summary>State the abort spin-down is heading for, or null when none is running.</summary>
    public WFCrackState? PendingAbort;

    /// <summary>Centrifuge spin as a fraction of full, 0 to 1.</summary>
    public float Spin;

    /// <summary>True once the rotor is at full, with the design D25 hysteresis applied.</summary>
    public bool AtFull;

    /// <summary>Mass the hull's pooled gravgens are carrying.</summary>
    public float Load;

    /// <summary>Mass the hull's pooled gravgens can carry.</summary>
    public float Capacity;

    /// <summary>One row per projector on the hull, ordered by grid-local X.</summary>
    public List<WFProjectorRow> Projectors = new();

    /// <summary>Why BEGIN CRACK is refused; one locale key per flag on the hover list.</summary>
    public WFCrackBlocker Blockers;

    /// <summary>True when the target button should be enabled.</summary>
    public bool CanTarget;

    /// <summary>True when the untarget button should be enabled.</summary>
    public bool CanUntarget;

    /// <summary>True when the begin button should be enabled.</summary>
    public bool CanBegin;
}

/// <summary>One gravity projector on the hull, as the console shows it.</summary>
[Serializable, NetSerializable]
public record struct WFProjectorRow(Vector2 GridLocalPos, WFProjectorState State, bool Powered, bool Broken);
