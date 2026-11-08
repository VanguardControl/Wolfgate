using Content.Shared._CE.ZLevels.Flight;
using Content.Shared._CE.ZLevels.Flight.Components;
using Content.Shared.Popups;
using Robust.Shared.Network;

namespace Content.Shared._WF.Planets.Flight;

/// <summary>
/// Wings and z-flight jets don't work in a planet's orbit layer: it is vacuum, and flying between it and the air layer
/// below desynced the flyer. Flight can't start there and stops when a flyer reaches it.
/// </summary>
public sealed partial class WFOrbitFlightSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CEZFlyerComponent, CEStartFlightAttemptEvent>(OnStartAttempt);
        SubscribeLocalEvent<CEZFlyerComponent, EntParentChangedMessage>(OnParentChanged);
    }

    private void OnStartAttempt(EntityUid uid, CEZFlyerComponent comp, CEStartFlightAttemptEvent args)
    {
        if (!InOrbit(uid))
            return;

        args.Cancel();
        _popup.PopupClient(Loc.GetString("wf-orbit-flight-refused"), uid, uid);
    }

    /// <summary>Server only: stopping flight while the client applies a state would mispredict.</summary>
    private void OnParentChanged(Entity<CEZFlyerComponent> ent, ref EntParentChangedMessage args)
    {
        if (_net.IsClient || !ent.Comp.Active || !InOrbit(ent))
            return;

        // The flight system is abstract here, so it can't be a dependency.
        EntityManager.System<CESharedZFlightSystem>().DeactivateFlight((ent, ent.Comp));
        _popup.PopupEntity(Loc.GetString("wf-orbit-flight-stopped"), ent, ent);
    }

    private bool InOrbit(EntityUid uid)
    {
        return HasComp<WFOrbitLayerComponent>(Transform(uid).MapUid);
    }
}
