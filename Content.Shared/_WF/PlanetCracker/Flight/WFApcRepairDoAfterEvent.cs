using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetCracker.Flight;

[Serializable, NetSerializable]
public sealed partial class WFApcRepairDoAfterEvent : SimpleDoAfterEvent;
