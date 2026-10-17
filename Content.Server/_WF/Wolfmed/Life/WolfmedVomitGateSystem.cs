using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Mobs.Systems;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Raised on a body before <c>VomitSystem.Vomit</c> empties its stomach. Cancel to stop it.</summary>
[ByRefEvent]
public record struct WolfmedVomitAttemptEvent(bool Cancelled = false);

/// <summary>A wound host that is dead or in cardiac arrest does not vomit.</summary>
public sealed class WolfmedVomitGateSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundHostComponent, WolfmedVomitAttemptEvent>(OnVomitAttempt);
    }

    private void OnVomitAttempt(Entity<WoundHostComponent> ent, ref WolfmedVomitAttemptEvent args)
    {
        if (_mobState.IsDead(ent) || HasComp<WolfmedCardiacArrestComponent>(ent))
            args.Cancelled = true;
    }
}
