using Content.Shared.Stunnable;
using Content.Shared.Damage.Components;
using Content.Shared.Effects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.Damage.Systems;

public sealed partial class DamageOnHighSpeedImpactSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedColorFlashEffectSystem _color = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    // WOLFGATE: impact-sound budget over time, see WfImpactSoundAllowed.
    private const int WfImpactSoundsPerWindow = 6;
    private TimeSpan _wfSoundWindowEnd;
    private int _wfSoundsThisWindow;

    /// <summary>WOLFGATE: allows at most six impact thuds per second; damage is never throttled, only the sound.</summary>
    private bool WfImpactSoundAllowed()
    {
        if (_gameTiming.CurTime >= _wfSoundWindowEnd)
        {
            _wfSoundWindowEnd = _gameTiming.CurTime + TimeSpan.FromSeconds(1);
            _wfSoundsThisWindow = 0;
        }

        return ++_wfSoundsThisWindow <= WfImpactSoundsPerWindow;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DamageOnHighSpeedImpactComponent, StartCollideEvent>(HandleCollide);
    }

    private void HandleCollide(EntityUid uid, DamageOnHighSpeedImpactComponent component, ref StartCollideEvent args)
    {
        if (!args.OurFixture.Hard || !args.OtherFixture.Hard)
            return;

        if (!EntityManager.HasComponent<DamageableComponent>(uid))
            return;

        //TODO: This should solve after physics solves
        var speed = args.OurBody.LinearVelocity.Length();

        if (speed < component.MinimumSpeed)
            return;

        if (component.LastHit != null
            && (_gameTiming.CurTime - component.LastHit.Value).TotalSeconds < component.DamageCooldown)
            return;

        component.LastHit = _gameTiming.CurTime;

        if (_robustRandom.Prob(component.StunChance))
            _stun.TryStun(uid, TimeSpan.FromSeconds(component.StunSeconds), true);

        var damageScale = component.SpeedDamageFactor * speed / component.MinimumSpeed;

        _damageable.TryChangeDamage(uid, component.Damage * damageScale);

        if (_gameTiming.IsFirstTimePredicted)
            if (WfImpactSoundAllowed()) // WOLFGATE: a skidding hull throws every loose item aboard into a wall at once; cap overlapping thuds over time or the client runs out of audio sources.
                _audio.PlayPvs(component.SoundHit, uid, AudioParams.Default.WithVariation(0.125f).WithVolume(-0.125f));
        _color.RaiseEffect(Color.Red, new List<EntityUid>() { uid }, Filter.Pvs(uid, entityManager: EntityManager));
    }

    public void ChangeCollide(EntityUid uid, float minimumSpeed, float stunSeconds, float damageCooldown, float speedDamage, DamageOnHighSpeedImpactComponent? collide = null)
    {
        if (!Resolve(uid, ref collide, false))
            return;

        collide.MinimumSpeed = minimumSpeed;
        collide.StunSeconds = stunSeconds;
        collide.DamageCooldown = damageCooldown;
        collide.SpeedDamageFactor = speedDamage;
        Dirty(uid, collide);
    }
}
