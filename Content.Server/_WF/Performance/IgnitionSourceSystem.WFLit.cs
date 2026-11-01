using Content.Shared.IgnitionSource;

namespace Content.Server.IgnitionSource;

/// <summary>
/// Tracks lit ignition sources, so the per-tick pass doesn't walk every unlit lighter, torch and welder in the round.
/// </summary>
public sealed partial class IgnitionSourceSystem
{
    /// <summary>Ignition sources that are lit right now.</summary>
    public readonly HashSet<EntityUid> WFLit = new();

    private readonly List<EntityUid> _wfLitWalk = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IgnitionSourceComponent, ComponentStartup>(WFOnStartup);
        SubscribeLocalEvent<IgnitionSourceComponent, ComponentShutdown>(WFOnShutdown);
    }

    private void WFOnStartup(Entity<IgnitionSourceComponent> ent, ref ComponentStartup args)
    {
        WFIgnitedChanged(ent, ent.Comp.Ignited);
    }

    private void WFOnShutdown(Entity<IgnitionSourceComponent> ent, ref ComponentShutdown args)
    {
        WFLit.Remove(ent);
    }

    protected override void WFIgnitedChanged(EntityUid uid, bool ignited)
    {
        if (ignited)
            WFLit.Add(uid);
        else
            WFLit.Remove(uid);
    }
}
