using System.Linq;
using System.Numerics;
using Content.Server.Power.EntitySystems;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.AsteroidMining;
using Content.Shared.ActionBlocker;
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
/// Gunnery consoles set the lasers' power and get a scan of the rock they are working.
/// </summary>
public sealed partial class WFMiningBeamSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private WFAsteroidSystem _asteroids = default!;

    /// <summary>Seconds over the overcharge line before a rock blows up.</summary>
    public const float OverchargeDuration = 2.5f;

    /// <summary>Displayed heat at which a rock starts to overcharge.</summary>
    public const float OverchargeLine = 1f;

    private static readonly TimeSpan UiInterval = TimeSpan.FromSeconds(0.25);
    private TimeSpan _nextUi;
    private readonly HashSet<EntityUid> _heated = new();
    private readonly List<EntityUid> _heatedList = new();
    /// <summary>The rock each open gunnery console last showed, kept while it cools, and whether it had lasers.</summary>
    private readonly Dictionary<EntityUid, (EntityUid? Rock, bool Lasers)> _gunnery = new();

    public override void Initialize()
    {
        base.Initialize();
        // The gunnery console's own systems handle everything else on this interface; only laser power is taken here.
        Subs.BuiEvents<FireControlConsoleComponent>(FireControlConsoleUiKey.Key, subs =>
        {
            subs.Event<WFMiningBeamPowerMessage>(OnPowerMessage);
        });
        SubscribeLocalEvent<WFMiningBeamEmitterComponent, ComponentShutdown>(OnEmitterShutdown);
        SubscribeLocalEvent<WFMiningBeamEmitterComponent, AttemptShootEvent>(OnAttemptShoot);
        // After reflection, which may cancel the pulse, and before damage, so rocks are heated rather than chipped.
        SubscribeLocalEvent<WFMiningPulseComponent, HitscanRaycastFiredEvent>(OnPulse,
            after: new[] { typeof(HitscanReflectSystem) }, before: new[] { typeof(HitscanBasicDamageSystem) });
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

    private void OnPowerMessage(Entity<FireControlConsoleComponent> ent, ref WFMiningBeamPowerMessage args)
    {
        if (!float.IsFinite(args.Power) || GunneryGrid(ent) is not { } grid || !_blocker.CanInteract(args.Actor, ent))
            return;

        var power = MathF.Round(Math.Clamp(args.Power, 0f, 1f), 2);
        foreach (var (uid, beam) in ShipLasers(grid))
        {
            beam.Power = power;
            Dirty(uid, beam);
        }

        UpdateGunnery(ent);
    }

    /// <summary>The ship a working gunnery console speaks for.</summary>
    private EntityUid? GunneryGrid(EntityUid console)
    {
        var xform = Transform(console);
        return xform.Anchored && this.IsPowered(console, EntityManager) ? xform.GridUid : null;
    }

    /// <summary>Every mining laser on a ship.</summary>
    public List<(EntityUid Uid, WFMiningBeamEmitterComponent Beam)> ShipLasers(EntityUid grid)
    {
        var lasers = new List<(EntityUid Uid, WFMiningBeamEmitterComponent Beam)>();
        var query = EntityQueryEnumerator<WFMiningBeamEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var beam, out var xform))
        {
            if (xform.GridUid == grid)
                lasers.Add((uid, beam));
        }

        return lasers;
    }

    /// <summary>Points the beam at whatever the pulse hit, and takes asteroids out of the hit list so they heat instead.</summary>
    private void OnPulse(Entity<WFMiningPulseComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled || !TryComp<WFMiningBeamEmitterComponent>(args.Gun, out var beam))
            return;

        EntityUid? rock = null;
        foreach (var hit in args.HitEntities)
        {
            if (TryComp<WFAsteroidComponent>(hit, out var asteroid) && !asteroid.Cracking)
            {
                rock = hit;
                break;
            }
        }

        if (rock is { } target)
            args.HitEntities.Remove(target);

        var direction = args.ShotDirection.LengthSquared() > 0f ? Vector2.Normalize(args.ShotDirection) : beam.BeamDirection;
        Pulse(args.Gun, beam, rock, args.DistanceTried, direction);
    }

    /// <summary>
    /// Records a pulse: lights the beam along the shot, which fire control aims at the cursor, and on an asteroid keeps
    /// the gun's power on it.
    /// </summary>
    public void Pulse(EntityUid gun, WFMiningBeamEmitterComponent beam, EntityUid? rock, float distance, Vector2 direction)
    {
        beam.LastPulse = _timing.CurTime;
        var wasActive = beam.Active;
        // Resend only on a visible change: about a degree of swing or a third of a metre of length.
        var changed = !beam.Active || beam.Target != rock ||
                      Vector2.Dot(beam.BeamDirection, direction) < 0.9998f ||
                      MathF.Abs(beam.BeamLength - distance) > 0.3f;
        beam.Active = true;
        beam.Target = rock;
        beam.BeamDirection = direction;
        beam.BeamLength = distance;
        if (rock is { } target)
            _heated.Add(target);

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
        comp.InWindow = false;
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
                if (beam.InWindow)
                {
                    beam.InWindow = false;
                    Dirty(uid, beam);
                }
                continue;
            }

            beam.Contribution = beam.Power * beam.Strength;
            asteroid.IncomingPower += beam.Contribution;
            asteroid.WindowScale = MathF.Max(asteroid.WindowScale, beam.WindowScale);
            _heated.Add(rock);

            var (low, high) = Window(asteroid);
            var inWindow = asteroid.Charge >= low && asteroid.Charge <= high;
            if (MathF.Abs(beam.Charge - asteroid.Charge) > 0.03f || beam.InWindow != inWindow)
            {
                beam.Charge = asteroid.Charge;
                beam.InWindow = inWindow;
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
        var open = new HashSet<EntityUid>();
        var consoles = EntityQueryEnumerator<FireControlConsoleComponent>();
        while (consoles.MoveNext(out var uid, out _))
        {
            if (!_ui.IsUiOpen(uid, FireControlConsoleUiKey.Key))
                continue;

            open.Add(uid);
            UpdateGunnery(uid);
        }

        foreach (var uid in _gunnery.Keys.Where(uid => !open.Contains(uid)).ToArray())
        {
            _gunnery.Remove(uid);
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
        if (MathF.Abs(charge - comp.Charge) > 0.02f || overcharging != comp.Overcharging ||
            MathF.Abs(comp.Progress - comp.SentProgress) > 0.04f)
        {
            comp.Charge = charge;
            comp.Overcharging = overcharging;
            comp.SentProgress = comp.Progress;
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

    /// <summary>
    /// Sends a gunnery console its ship's laser power and a scan of the rock its lasers are on, or were last on while
    /// it cools. A ship without mining lasers is sent one empty state, which hides the panel.
    /// </summary>
    private void UpdateGunnery(EntityUid console)
    {
        var known = _gunnery.TryGetValue(console, out var last);
        var lasers = GunneryGrid(console) is { } grid ? ShipLasers(grid) : new List<(EntityUid Uid, WFMiningBeamEmitterComponent Beam)>();
        if (lasers.Count == 0)
        {
            if (!known || last.Lasers)
                _ui.ServerSendUiMessage(console, FireControlConsoleUiKey.Key,
                    new WFMiningBeamGunneryUpdateMessage(new WFMiningBeamGunneryState(0, 0f, 0f, null)));
            _gunnery[console] = (null, false);
            return;
        }

        var rock = lasers.Select(l => l.Beam.Target).FirstOrDefault(t => t != null) ?? last.Rock;
        WFMiningBeamTargetEntry? target = null;
        if (rock is { } uid && !TerminatingOrDeleted(uid) && TryComp<WFAsteroidComponent>(uid, out var comp) && !comp.Cracking)
        {
            var on = lasers.Where(l => l.Beam.Active && l.Beam.Target == uid).ToList();
            var strength = (on.Count > 0 ? on : lasers).Sum(l => l.Beam.Strength);
            var (low, high) = Window(comp);
            var ores = comp.Ores.Select(o => new WFMiningBeamOreEntry(OreName(o.Ore), o.Fraction, o.Color)).ToArray();
            target = new WFMiningBeamTargetEntry(Name(uid), comp.Mass, ores, comp.Resistance, low, high, comp.Charge,
                Math.Clamp(comp.Progress, 0f, 1f), comp.OverchargeTime / OverchargeDuration, comp.IncomingPower,
                strength, on.Count > 0);
        }
        else
        {
            rock = null;
        }

        var cooldown = lasers.Max(l => MathF.Max(0f, (float) (l.Beam.CooldownUntil - _timing.CurTime).TotalSeconds));
        _gunnery[console] = (rock, true);
        _ui.ServerSendUiMessage(console, FireControlConsoleUiKey.Key, new WFMiningBeamGunneryUpdateMessage(
            new WFMiningBeamGunneryState(lasers.Count, lasers[0].Beam.Power, cooldown, target)));
    }

    private string OreName(ProtoId<OrePrototype> ore)
    {
        return _proto.TryIndex(ore, out var proto) && proto.OreEntity is { } entity &&
               _proto.TryIndex(entity, out var entityProto)
            ? entityProto.Name
            : ore.Id;
    }
}
