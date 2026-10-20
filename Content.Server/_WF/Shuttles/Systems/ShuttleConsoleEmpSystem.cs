using Content.Server.Shuttles.Components;
using Content.Shared.Emp;

namespace Content.Server._WF.Shuttles.Systems;

/// <summary>
/// Makes shuttle consoles immune to EMP, so a pulse can't cut a pilot off from the helm.
/// </summary>
public sealed class ShuttleConsoleEmpSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShuttleConsoleComponent, EmpAttemptEvent>(OnEmpAttempt);
    }

    private void OnEmpAttempt(Entity<ShuttleConsoleComponent> ent, ref EmpAttemptEvent args)
    {
        args.Cancelled = true;
    }
}
