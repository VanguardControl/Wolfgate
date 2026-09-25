using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>Raised on the surveyor when its scan DoAfter ends; the server half does the revealing.</summary>
[Serializable, NetSerializable]
public sealed partial class WFSurveyScanDoAfterEvent : SimpleDoAfterEvent;
