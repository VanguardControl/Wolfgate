using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;
using Content.Server._CE.ZLevels.Core;
using Content.Server.Power.Components;
using Content.Shared._WF.PlanetCracker.Cracker;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>
/// Turns the centrifuge's raw charge into the spin, at-full and pooled-load values the rotor dial and the crack
/// console draw, with the design D25 hysteresis applied once here rather than in each window.
/// Declares no subscriptions on purpose: ChargedMachineActivatedEvent and ChargedMachineDeactivatedEvent are already
/// claimed on GravityGeneratorComponent by GravityGeneratorSystem, and nothing here needs an edge - only the level.
/// The charge read is cross-type but legal: PowerChargeComponent's [Access] leaves other types Read.
/// </summary>
public sealed partial class WFCentrifugeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    /// <summary>The generator spooling up from a standstill.</summary>
    public static readonly SoundSpecifier StartSound = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/gravitic_generator_start_effect.ogg");

    /// <summary>The two running loops, both played together and both scaled by spin.</summary>
    public static readonly SoundSpecifier HumSound1 = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/gravitic_generator_loop_1.ogg");
    public static readonly SoundSpecifier HumSound2 = new SoundPathSpecifier("/Audio/_WF/PlanetCracker/Crack/gravitic_generator_loop_2.ogg");

    /// <summary>Loop volume at a standstill, in dB; zero at full spin.</summary>
    private const float HumQuiet = -20f;

    /// <summary>
    /// Spin movement that is worth a state send. Deliberately coarse: at the shipped chargeRate of 0.00417 a sweep
    /// moves Charge by 0.00104, so a finer epsilon would dirty the component on every sweep for the whole 240 s
    /// spin-up, and this is still far finer than the 0.98/0.95 hysteresis band.
    /// </summary>
    private const float SpinEpsilon = 0.005f;

    /// <summary>Next tick of the 4 Hz sweep.</summary>
    private TimeSpan _nextSweep;

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + TimeSpan.FromSeconds(0.25);

        var query = EntityQueryEnumerator<WFCentrifugeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!TryComp<PowerChargeComponent>(uid, out var charge))
                continue;

            // A fraction of the machine's own MaxCharge, never the absolute charge: the thresholds are fractions too.
            var spin = charge.MaxCharge > 0f ? charge.Charge / charge.MaxCharge : 0f;

            // Design D25: latches on at FullOn and only lets go below FullOff, so a dial hovering on the line does
            // not arm and disarm the grace timer every sweep.
            var atFull = comp.AtFull ? spin >= comp.FullOff : spin >= comp.FullOn;

            UpdateHum(uid, comp, spin);

            var load = 0f;
            var capacity = 0f;

            // The pooled figure for the whole hull, which already folds in the D11 virtual mass, so the readout and
            // the rule that drops the ship agree by construction.
            if (_zLevels.TryGetGravgenLoad(Transform(uid).ParentUid, out var mass, out var cap))
            {
                load = mass;
                capacity = cap;
            }

            // Compared against the last values sent, not against a value written this sweep, or the spin would creep
            // past the epsilon a sliver at a time and never dirty at all.
            var spinMoved = MathF.Abs(spin - comp.Spin) >= SpinEpsilon;
            var changed = atFull != comp.AtFull
                || !comp.Load.Equals(load)
                || !comp.Capacity.Equals(capacity);

            if (!spinMoved && !changed)
                continue;

            comp.Spin = spin;
            comp.AtFull = atFull;
            comp.Load = load;
            comp.Capacity = capacity;
            Dirty(uid, comp);
        }
    }

    /// <summary>
    /// Runs the two spin loops: started the moment the rotor leaves a standstill, with the spool-up effect on top,
    /// quiet at first and full at full spin, and stopped again once it is still.
    /// </summary>
    private void UpdateHum(EntityUid uid, WFCentrifugeComponent comp, float spin)
    {
        if (spin <= 0f)
        {
            comp.HumStream1 = _audio.Stop(comp.HumStream1);
            comp.HumStream2 = _audio.Stop(comp.HumStream2);
            return;
        }

        var volume = MathHelper.Lerp(HumQuiet, 0f, Math.Clamp(spin, 0f, 1f));

        if (comp.HumStream1 == null)
        {
            // comp.Spin is the last spin sent, so it still reads zero on the sweep the rotor first moves.
            if (comp.Spin <= 0f)
                _audio.PlayPvs(StartSound, uid);

            var loop = AudioParams.Default.WithLoop(true).WithVolume(volume);
            comp.HumStream1 = _audio.PlayPvs(HumSound1, uid, loop)?.Entity;
            comp.HumStream2 = _audio.PlayPvs(HumSound2, uid, loop)?.Entity;
            return;
        }

        _audio.SetVolume(comp.HumStream1, volume);
        _audio.SetVolume(comp.HumStream2, volume);
    }
}
