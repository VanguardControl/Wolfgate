using System.Linq;
using System.Numerics;
using Content.Server.Gatherable.Components;
using Content.Server.Power.Components;
using Content.Shared._WF.AsteroidMining;
using Content.Shared.Mining;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Hitscan.Systems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.AsteroidMining;

/// <summary>
/// Runs ship mining lasers. Each pulse that hits an asteroid keeps the gun's power on it; a rock that stays inside its
/// window long enough cracks cleanly, and one held over the overcharge line blows up and knocks its guns offline.
/// </summary>
public sealed partial class WFMiningBeamSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WFAsteroidSystem _asteroids = default!;

    /// <summary>Seconds over the overcharge line before a rock blows up.</summary>
    public const float OverchargeDuration = 2.5f;

    /// <summary>Displayed heat at which a rock starts to overcharge.</summary>
    public const float OverchargeLine = 1f;

    private static readonly TimeSpan UiInterval = TimeSpan.FromSeconds(0.25);
    private TimeSpan _nextUi;
    private readonly HashSet<EntityUid> _heated = new();
    private readonly List<EntityUid> _heatedList = new();
    private readonly HashSet<Entity<WFAsteroidComponent>> _rocks = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFMiningBeamConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<WFMiningBeamConsoleComponent, WFMiningBeamPowerMessage>(OnPowerMessage);
        SubscribeLocalEvent<WFMiningBeamEmitterComponent, ComponentShutdown>(OnEmitterShutdown);
        SubscribeLocalEvent<WFMiningBeamEmitterComponent, AttemptShootEvent>(OnAttemptShoot);
        // After reflection, which may cancel the pulse, and before damage, so rocks are heated rather than chipped.
        SubscribeLocalEvent<WFMiningPulseComponent, HitscanRaycastFiredEvent>(OnPulse,
            after: new[] { typeof(HitscanReflectSystem) }, before: new[] { typeof(HitscanBasicDamageSystem) });
    }

    private void OnUiOpened(Entity<WFMiningBeamConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnEmitterShutdown(Entity<WFMiningBeamEmitterComponent> ent, ref ComponentShutdown args)
    {
        Stop(ent, ent.Comp);
    }

    private void OnAttemptShoot(Entity<WFMiningBeamEmitterComponent> ent, ref AttemptShootEvent args)
    {
        if (_timing.CurTime < ent.Comp.CooldownUntil)
            args.Cancelled = true;
    }

    private void OnPowerMessage(Entity<WFMiningBeamConsoleComponent> ent, ref WFMiningBeamPowerMessage args)
    {
        if (!_ui.IsUiOpen(ent.Owner, WFMiningBeamUiKey.Key, args.Actor) ||
            !TryComp<ApcPowerReceiverComponent>(ent, out var power) || !power.Powered ||
            Transform(ent).GridUid is not { } grid ||
            !TryGetEntity(args.Emitter, out var uid) ||
            !TryComp<WFMiningBeamEmitterComponent>(uid, out var beam) ||
            Transform(uid.Value).GridUid != grid || !float.IsFinite(args.Power))
            return;

        beam.Power = MathF.Round(Math.Clamp(args.Power, 0f, 1f), 2);
        Dirty(uid.Value, beam);
        UpdateUi(ent);
    }

    /// <summary>Points the beam at whatever the pulse hit, and takes asteroids out of the hit list so they heat instead.</summary>
    private void OnPulse(Entity<WFMiningPulseComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled || !TryComp<WFMiningBeamEmitterComponent>(args.Gun, out var beam))
            return;

        EntityUid? rock = null;
        foreach (var hit in args.HitEntities)
        {
            // Boulders stay in the list, so the pulse's gathering breaks them open.
            if (TryComp<WFAsteroidComponent>(hit, out var asteroid) && !asteroid.Cracking && !HasComp<GatherableComponent>(hit))
            {
                rock = hit;
                break;
            }
        }

        if (rock is { } target)
            args.HitEntities.Remove(target);

        Pulse(args.Gun, beam, rock, args.DistanceTried);
    }

    /// <summary>Records a pulse: lights the beam and, on an asteroid, keeps the gun's power on it.</summary>
    public void Pulse(EntityUid gun, WFMiningBeamEmitterComponent beam, EntityUid? rock, float distance)
    {
        beam.LastPulse = _timing.CurTime;
        var wasActive = beam.Active;
        var changed = !beam.Active || beam.Target != rock;
        beam.Active = true;
        beam.Target = rock;
        if (rock is { } target && TryComp<WFAsteroidComponent>(target, out var asteroid))
        {
            var xform = Transform(target);
            beam.TargetGrid = xform.GridUid ?? target;
            beam.TargetCenter = xform.GridUid != null ? xform.LocalPosition : Vector2.Zero;
            beam.TargetRadius = asteroid.Radius;
            _heated.Add(target);
        }
        else
        {
            beam.TargetGrid = null;
            changed |= MathF.Abs(beam.BeamLength - distance) > 0.5f;
            beam.BeamLength = distance;
        }

        if (changed)
            Dirty(gun, beam);
        if (!wasActive)
            beam.Audio = _audio.PlayPvs(beam.LoopSound, gun)?.Entity;
    }

    /// <summary>Puts a gun's beam out.</summary>
    public void Stop(EntityUid gun, WFMiningBeamEmitterComponent comp)
    {
        comp.Audio = _audio.Stop(comp.Audio);
        comp.Contribution = 0f;
        if (!comp.Active && comp.Target == null)
            return;

        comp.Active = false;
        comp.Target = null;
        comp.TargetGrid = null;
        comp.Charge = 0f;
        if (!TerminatingOrDeleted(gun))
            Dirty(gun, comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var rock in _heated)
        {
            if (TryComp<WFAsteroidComponent>(rock, out var comp))
            {
                comp.IncomingPower = 0f;
                comp.WindowScale = 1f;
            }
        }

        var now = _timing.CurTime;
        var emitters = EntityQueryEnumerator<WFMiningBeamEmitterComponent>();
        while (emitters.MoveNext(out var uid, out var beam))
        {
            if (!beam.Active)
                continue;

            if (now - beam.LastPulse > TimeSpan.FromSeconds(beam.ActiveTime))
            {
                Stop(uid, beam);
                continue;
            }

            if (beam.Target is not { } rock || TerminatingOrDeleted(rock) ||
                !TryComp<WFAsteroidComponent>(rock, out var asteroid) || asteroid.Cracking)
            {
                beam.Contribution = 0f;
                continue;
            }

            beam.Contribution = beam.Power * beam.Strength;
            asteroid.IncomingPower += beam.Contribution;
            asteroid.WindowScale = MathF.Max(asteroid.WindowScale, beam.WindowScale);
            _heated.Add(rock);

            if (MathF.Abs(beam.Charge - asteroid.Charge) > 0.03f)
            {
                beam.Charge = asteroid.Charge;
                Dirty(uid, beam);
            }
        }

        _heatedList.Clear();
        _heatedList.AddRange(_heated);
        foreach (var rock in _heatedList)
        {
            if (TerminatingOrDeleted(rock) || !TryComp<WFAsteroidComponent>(rock, out var comp) || comp.Cracking ||
                !Heat(rock, comp, frameTime))
                _heated.Remove(rock);
        }

        if (now < _nextUi)
            return;

        _nextUi = now + UiInterval;
        var consoles = EntityQueryEnumerator<WFMiningBeamConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var console))
        {
            if (_ui.IsUiOpen(uid, WFMiningBeamUiKey.Key))
                UpdateUi((uid, console));
        }
    }

    /// <summary>The window as shown, after the beams' window scale.</summary>
    public static (float Low, float High) Window(WFAsteroidComponent comp)
    {
        var half = comp.WindowHalfWidth * comp.WindowScale;
        return (comp.WindowCenter - half, comp.WindowCenter + half);
    }

    /// <summary>Advances a rock's heat by a tick. Returns false once it has cooled and no beam is on it.</summary>
    private bool Heat(EntityUid rock, WFAsteroidComponent comp, float frameTime)
    {
        var heating = comp.IncomingPower > 0f;
        var equilibrium = comp.IncomingPower * (1f - comp.Resistance);
        // Heavier rocks heat and cool more slowly; with no beam they cool at half speed.
        var tau = (1.2f + comp.Mass * 0.01f) * (heating ? 1f : 2f);
        comp.BaseCharge += (equilibrium - comp.BaseCharge) * (1f - MathF.Exp(-frameTime / tau));

        // Unstable rocks drift slowly around the beam's heat, so the power has to follow them.
        var time = (float) _timing.CurTime.TotalSeconds;
        var wobble = heating
            ? comp.Instability * 0.12f * (0.6f * MathF.Sin(time * 0.9f + comp.NoiseSeed) +
                                          0.4f * MathF.Sin(time * 0.37f + comp.NoiseSeed * 1.7f))
            : 0f;
        var charge = MathF.Max(0f, comp.BaseCharge + wobble);
        var (low, high) = Window(comp);

        if (heating && charge >= low && charge <= high)
            comp.Progress += frameTime / WFAsteroidSystem.CrackTime(comp);
        else
            comp.Progress = MathF.Max(0f, comp.Progress - frameTime * 0.04f);

        comp.OverchargeTime = charge >= OverchargeLine
            ? comp.OverchargeTime + frameTime
            : MathF.Max(0f, comp.OverchargeTime - frameTime);

        var overcharging = comp.OverchargeTime > 0f;
        if (MathF.Abs(charge - comp.Charge) > 0.02f || overcharging != comp.Overcharging)
        {
            comp.Charge = charge;
            comp.Overcharging = overcharging;
            Dirty(rock, comp);
        }

        if (comp.OverchargeTime >= OverchargeDuration)
        {
            Overload(rock);
            _asteroids.QueueCrack(rock, WFAsteroidCrack.Overcharge, comp);
            return false;
        }

        if (comp.Progress >= 1f)
        {
            _asteroids.QueueCrack(rock, WFAsteroidCrack.Clean, comp);
            return false;
        }

        if (heating || comp.BaseCharge > 0.01f || comp.Progress > 0f)
            return true;

        comp.BaseCharge = 0f;
        comp.Charge = 0f;
        comp.Overcharging = false;
        Dirty(rock, comp);
        return false;
    }

    /// <summary>Knocks every gun on a rock offline for its overload cooldown.</summary>
    private void Overload(EntityUid rock)
    {
        var emitters = EntityQueryEnumerator<WFMiningBeamEmitterComponent>();
        while (emitters.MoveNext(out var uid, out var beam))
        {
            if (beam.Target != rock)
                continue;

            beam.CooldownUntil = _timing.CurTime + TimeSpan.FromSeconds(beam.OverloadCooldown);
            Stop(uid, beam);
        }
    }

    private void UpdateUi(Entity<WFMiningBeamConsoleComponent> console)
    {
        var xform = Transform(console);
        var connected = TryComp<ApcPowerReceiverComponent>(console, out var power) && power.Powered &&
                        xform.GridUid != null;
        if (!connected || xform.GridUid is not { } grid)
        {
            _ui.SetUiState(console.Owner, WFMiningBeamUiKey.Key,
                new WFMiningBeamConsoleBoundUserInterfaceState(false, console.Comp.Range,
                    Array.Empty<WFMiningBeamEmitterEntry>(), Array.Empty<WFMiningBeamTargetEntry>()));
            return;
        }

        var origin = _transform.GetWorldPosition(xform);
        var toGrid = -_transform.GetWorldRotation(grid);

        var emitters = new List<WFMiningBeamEmitterEntry>();
        var query = EntityQueryEnumerator<WFMiningBeamEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var beam, out var beamXform))
        {
            if (beamXform.GridUid != grid)
                continue;

            var (_, rotation, matrix) = _transform.GetWorldPositionRotationMatrix(uid);
            var muzzle = Vector2.Transform(beam.MuzzleOffset, matrix);
            var forward = rotation.RotateVec(beam.Forward);
            var cooldown = MathF.Max(0f, (float) (beam.CooldownUntil - _timing.CurTime).TotalSeconds);
            emitters.Add(new WFMiningBeamEmitterEntry(GetNetEntity(uid), Name(uid), GetNetEntity(beam.Target),
                beam.Active, beam.Power, beam.Strength, beam.MaxRange, cooldown,
                toGrid.RotateVec(muzzle - origin), toGrid.RotateVec(forward)));
        }

        var targets = new List<WFMiningBeamTargetEntry>();
        _rocks.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(xform), console.Comp.Range, _rocks);
        foreach (var (uid, rock) in _rocks)
        {
            if (rock.Cracking || rock.Mass <= 0f || Transform(uid).GridUid == grid)
                continue;

            var (low, high) = Window(rock);
            var ores = rock.Ores.Select(o => new WFMiningBeamOreEntry(OreName(o.Ore), o.Fraction, o.Color)).ToArray();
            targets.Add(new WFMiningBeamTargetEntry(GetNetEntity(uid), Name(uid),
                toGrid.RotateVec(_transform.GetWorldPosition(uid) - origin), rock.Radius, rock.Mass, ores,
                rock.Resistance, rock.Instability, low, high, rock.Charge, Math.Clamp(rock.Progress, 0f, 1f),
                rock.OverchargeTime / OverchargeDuration, rock.IncomingPower, rock.VeinColor));
        }

        targets.Sort((a, b) => a.Position.LengthSquared().CompareTo(b.Position.LengthSquared()));
        _ui.SetUiState(console.Owner, WFMiningBeamUiKey.Key,
            new WFMiningBeamConsoleBoundUserInterfaceState(true, console.Comp.Range, emitters.ToArray(), targets.ToArray()));
    }

    private string OreName(ProtoId<OrePrototype> ore)
    {
        return _proto.TryIndex(ore, out var proto) && proto.OreEntity is { } entity &&
               _proto.TryIndex(entity, out var entityProto)
            ? entityProto.Name
            : ore.Id;
    }
}
