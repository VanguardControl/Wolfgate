using Content.Shared.Movement.Systems;

namespace Content.Shared._WF.GroundCover;

/// <summary>Slows mobs in deep snow, on both sides so walking stays predicted.</summary>
public sealed class WFSnowSlowSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSnowSlowedComponent, RefreshMovementSpeedModifiersEvent>(OnRefresh);
        SubscribeLocalEvent<WFSnowSlowedComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WFSnowSlowedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFSnowSlowedComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>Sets a mob's speed share for the snow under it; 1 removes the slowdown.</summary>
    public void SetSpeed(EntityUid uid, float speed)
    {
        if (speed >= 1f)
        {
            RemComp<WFSnowSlowedComponent>(uid);
            return;
        }

        var comp = EnsureComp<WFSnowSlowedComponent>(uid);
        if (MathF.Abs(comp.Speed - speed) < 0.001f)
            return;

        comp.Speed = speed;
        Dirty(uid, comp);
        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefresh(Entity<WFSnowSlowedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.ModifySpeed(ent.Comp.Speed);
    }

    private void OnState(Entity<WFSnowSlowedComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _speed.RefreshMovementSpeedModifiers(ent);
    }

    private void OnStartup(Entity<WFSnowSlowedComponent> ent, ref ComponentStartup args)
    {
        _speed.RefreshMovementSpeedModifiers(ent);
    }

    private void OnShutdown(Entity<WFSnowSlowedComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            _speed.RefreshMovementSpeedModifiers(ent);
    }
}
