using Content.Server._EinsteinEngines.Silicon.Charge;
using Content.Server._EinsteinEngines.Silicon.Death;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.PowerCell.Components;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>
/// A mechanical body's cardiac arrest. No blood and no breathing means no oxygenation clock, so a machine
/// that loses its power or its pump simply stops: unconscious, indefinitely, until somebody fixes it, and
/// with nothing running out in the meantime. Death is still the brain, which for a chassis is the
/// positronic one, and that goes through the same organ path a fleshy brain does.
/// </summary>
public sealed class WolfmedShutdownSystem : EntitySystem
{
    /// <summary>Pressure key for a machine with nothing running.</summary>
    public const string ShutdownPressure = "shutdown";

    /// <summary>How often a chassis that is already down re-checks that it should still be.</summary>
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SiliconChargeSystem _siliconCharge = default!;

    private TimeSpan _nextReconcile;

    public override void Initialize()
    {
        base.Initialize();

        // Both are raised directed on the chassis, and the SiliconDownOnDead pair is free: the only other
        // subscriber keys off SiliconEmitSoundOnDrained.
        SubscribeLocalEvent<SiliconDownOnDeadComponent, SiliconChargeDeathEvent>(OnChargeDeath);
        SubscribeLocalEvent<SiliconDownOnDeadComponent, SiliconChargeAliveEvent>(OnChargeAlive);
        SubscribeLocalEvent<WolfmedRejuvenateEvent>(OnRejuvenate);
    }

    /// <summary>
    /// Mechanical: a wound host that is a machine and runs no oxygenation clock. Both halves matter. A body
    /// that simply has no brain organ is a test fixture or a species Wolfmed does not model, not a chassis,
    /// and shutting one of those down would be the brainless-poll bug in a different costume.
    /// </summary>
    public bool IsMechanical(EntityUid body)
    {
        if (!_consciousness.OwnsMobState(body) || !HasComp<SiliconComponent>(body))
            return false;

        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (HasComp<WolfmedBrainComponent>(organ))
                return false;
        }

        return true;
    }

    public bool IsShutDown(EntityUid body) => HasComp<WolfmedShutdownComponent>(body);

    /// <summary>
    /// Only bodies that are already down are polled, which is normally none of them. A shutdown is written
    /// as an external pressure, and anything that resets a body wholesale (a rejuvenate clears every
    /// pressure it finds) can leave the flag on a chassis that is walking around: the readout would then
    /// show STANDBY over a machine in a firefight. The cause is re-read here instead of trusted.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextReconcile)
            return;

        _nextReconcile = _timing.CurTime + ReconcileInterval;

        var query = EntityQueryEnumerator<WolfmedShutdownComponent>();
        while (query.MoveNext(out var body, out _))
        {
            if (!TerminatingOrDeleted(body) && IsMechanical(body))
            {
                Refresh(body);
                continue;
            }

            // Not a chassis any more, so nothing here owns it.
            RemComp<WolfmedShutdownComponent>(body);
            _consciousness.SetExternalPressure(body, ShutdownPressure, 0f);
        }
    }

    private void OnChargeDeath(EntityUid uid, SiliconDownOnDeadComponent comp, SiliconChargeDeathEvent args) =>
        Refresh(uid, false);

    private void OnChargeAlive(EntityUid uid, SiliconDownOnDeadComponent comp, SiliconChargeAliveEvent args) =>
        Refresh(uid, true);

    private void OnRejuvenate(ref WolfmedRejuvenateEvent args)
    {
        RestoreCell(args.Target);
        Refresh(args.Target);
    }

    /// <summary>
    /// Playtest 1: a full heal puts a missing cell back, the chassis prototype's own starting one, and tells
    /// the charge tracking at once so the shutdown clears on the same tick rather than on its next pass.
    /// </summary>
    public bool RestoreCell(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !TryComp(body, out SiliconComponent? silicon) ||
            !TryComp(body, out PowerCellSlotComponent? cellSlot) ||
            !_itemSlots.TryGetSlot(body, cellSlot.CellSlotId, out var slot) || slot.HasItem ||
            slot.StartingItem is not { } starting || slot.ContainerSlot is not { } container)
            return false;

        var cell = Spawn(starting, Transform(body).Coordinates);
        if (!_container.Insert(cell, container))
        {
            Del(cell);
            return false;
        }

        if (_siliconCharge.TryGetSiliconBattery(body, out var battery) && battery.MaxCharge > 0f)
            _siliconCharge.UpdateChargeState(body, (short) MathF.Round(battery.CurrentCharge / battery.MaxCharge * 10f), silicon);

        return true;
    }

    /// <summary>
    /// Re-reads power and pump and sets the shutdown state to match. Called by whoever already owns the
    /// event that could have changed one of them: the organ events are single-owner pairs. The charge
    /// events say what the power is, because they are raised while the flag behind it is being written.
    /// </summary>
    public void Refresh(EntityUid body, bool? powered = null)
    {
        if (TerminatingOrDeleted(body) || !IsMechanical(body))
            return;

        // The flag and the pressure are written every time, never skipped when the flag already agrees:
        // they are two halves of one state and only one of them survives a rejuvenate. SetExternalPressure
        // is itself a no-op when the level has not moved.
        var power = powered ?? HasPower(body);
        var down = !power || !HasPump(body);
        if (down)
        {
            // M1a: the reason the HUD and the alerts name. An empty cell is the one the player can fix.
            var shutdown = EnsureComp<WolfmedShutdownComponent>(body);
            var reason = power ? WolfmedCauseSource.Pump : WolfmedCauseSource.Power;
            if (shutdown.Reason != reason)
            {
                shutdown.Reason = reason;
                Dirty(body, shutdown);
                _consciousness.Refresh(body);
            }
        }
        else
        {
            RemComp<WolfmedShutdownComponent>(body);
        }

        _consciousness.SetExternalPressure(body, ShutdownPressure, down ? 1f : 0f);
    }

    /// <summary>A cell in the chassis with something left in it. SiliconDownOnDead already tracks both. M2: the restart reads it.</summary>
    public bool HasPower(EntityUid body) =>
        !TryComp(body, out SiliconDownOnDeadComponent? silicon) || !silicon.Dead;

    /// <summary>A coolant pump in the chassis that has not been destroyed. The analyzer's cooling line reads it too.</summary>
    public bool HasPump(EntityUid body)
    {
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (!HasComp<HeartComponent>(organ))
                continue;

            return CompOrNull<WolfmedOrganComponent>(organ)?.Health is not { } health ||
                   health > FixedPoint2.Zero;
        }

        return false;
    }
}
