using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;
using Content.Server._CE.ZLevels.Core;
using Content.Server.Power.Components;
using Content.Shared._WF.PlanetCracker.Cracker;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>Turns the centrifuge's charge into the spin, at-full and load values its dials draw.</summary>
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

    /// <summary>Spin change worth a state send; coarse so the slow spin-up doesn't dirty every sweep.</summary>
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

            // Latches on at FullOn and lets go only below FullOff, so a dial on the line doesn't flap the grace timer.
            var atFull = comp.AtFull ? spin >= comp.FullOff : spin >= comp.FullOn;

            UpdateHum(uid, comp, spin);

            var load = 0f;
            var capacity = 0f;

            // The pooled figure for the whole hull, so the readout matches the rule that drops the ship.
            if (_zLevels.TryGetGravgenLoad(Transform(uid).ParentUid, out var mass, out var cap))
            {
                load = mass;
                capacity = cap;
            }

            // Compared against the last values sent, or the spin could creep past the epsilon and never dirty.
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

    /// <summary>Runs the two spin loops, quiet to full by spin, with the spool-up effect as they start.</summary>
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
