using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WF.NetTraffic;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;

namespace Content.Server._WF.NetTraffic;

/// <summary>
/// Sends a ship's repair snapshot only to players who hold or wear a repair tool. The snapshot lists every tile and
/// fixture of the ship and is several times the size of the grid itself, and only the tool's preview reads it.
/// </summary>
public sealed partial class WFRepairDataNetSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private bool _toolOnly;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipRepairDataComponent, ComponentGetStateAttemptEvent>(OnGetStateAttempt);
        SubscribeLocalEvent<RepairDataEyeComponent, ComponentStartup>(OnToolUserStartup);
        Subs.CVar(_cfg, NetTrafficCVars.RepairDataToolOnly, OnToolOnlyChanged, true);
    }

    private void OnGetStateAttempt(Entity<ShipRepairDataComponent> ent, ref ComponentGetStateAttemptEvent args)
    {
        // No player is a replay recording, which keeps everything.
        if (!_toolOnly || args.Cancelled || args.Player is not { } player)
            return;

        if (!HasComp<RepairDataEyeComponent>(player.AttachedEntity))
            args.Cancelled = true;
    }

    /// <summary>A player has just picked a tool up, and has none of the snapshots they were not sent before.</summary>
    private void OnToolUserStartup(Entity<RepairDataEyeComponent> ent, ref ComponentStartup args)
    {
        if (_toolOnly)
            ResendAll();
    }

    private void OnToolOnlyChanged(bool value)
    {
        _toolOnly = value;
        if (!value)
            ResendAll();
    }

    /// <summary>Marks every snapshot changed, so each goes again to whoever is now allowed it.</summary>
    private void ResendAll()
    {
        var query = EntityQueryEnumerator<ShipRepairDataComponent>();
        while (query.MoveNext(out var uid, out var data))
        {
            Dirty(uid, data);
        }
    }
}
