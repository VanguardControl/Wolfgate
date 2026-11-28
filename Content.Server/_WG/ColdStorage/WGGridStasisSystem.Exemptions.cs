using Content.Server._NF.CryoSleep;
using Content.Server.Shuttles.Components;
using Content.Server.Spawners.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.Medical.Cryogenics;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;

namespace Content.Server._WG.ColdStorage;

/// <summary>What stasis never pauses, whatever grid it sits on.</summary>
public sealed partial class WGGridStasisSystem
{
    /// <summary>How much of an entity stasis leaves awake.</summary>
    private enum Exemption : byte
    {
        None,

        /// <summary>The entity only; what it holds may be paused.</summary>
        Self,

        /// <summary>The entity and everything inside it.</summary>
        Tree,
    }

    private readonly HashSet<EntityUid> _skipped = new();

    /// <summary>Docks, spawn points, cryo pods, minded bodies and deed holders: things that answer to players or the round while their grid sleeps.</summary>
    private Exemption ExemptionOf(EntityUid uid)
    {
        if (HasComp<DockingComponent>(uid)
            || HasComp<SpawnPointComponent>(uid)
            || HasComp<ContainerSpawnPointComponent>(uid)
            || HasComp<CryostorageComponent>(uid)
            || HasComp<CryoSleepComponent>(uid)
            || HasComp<CryoSleepFallbackComponent>(uid)
            || HasComp<CryoPodComponent>(uid)
            || _mobQuery.HasComp(uid) && TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
            return Exemption.Tree;

        return HasComp<ShuttleDeedComponent>(uid) && !_gridQuery.HasComp(uid) ? Exemption.Self : Exemption.None;
    }
}
