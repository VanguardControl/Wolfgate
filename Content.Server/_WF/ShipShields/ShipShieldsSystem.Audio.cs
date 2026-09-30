using System.Numerics;
using Content.Server._WF.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    [Dependency] private IGameTiming _wfShieldTiming = default!;
    [Dependency] private IRobustRandom _wfShieldRandom = default!;
    [Dependency] private EntityLookupSystem _wfShieldAudioLookup = default!;
    private static readonly SoundCollectionSpecifier WolfgateImpactSound = new("WFShipShieldImpacts");

    /// <summary>Plays one hull-wide echo that survives shield collapse.</summary>
    private void PlayWolfgateShieldImpact(EntityUid shield, Vector2 position)
    {
        if (!TryComp<ShipShieldComponent>(shield, out var shieldComponent) ||
            TerminatingOrDeleted(shieldComponent.Shielded))
            return;
        var grid = shieldComponent.Shielded;
        var state = EnsureComp<WFShipShieldImpactAudioComponent>(grid);
        var now = _wfShieldTiming.CurTime;
        if (now < state.NextImpactSound ||
            state.ActiveImpactSound is { } active && !TerminatingOrDeleted(active))
            return;
        state.NextImpactSound = now + TimeSpan.FromSeconds(_wfShieldRandom.NextFloat(2.4f, 3.6f));
        var bounds = _wfShieldAudioLookup.GetWorldAABB(grid);
        var center = _wfShieldMap.GetGridPosition(grid);
        var extent = Vector2.Max(Vector2.Abs(bounds.BottomLeft - center), Vector2.Abs(bounds.TopRight - center));
        var radius = MathF.Max(1f, extent.Length());
        var sound = _audio.PlayPvs(WolfgateImpactSound, grid,
            AudioParams.Default.WithVolume(-4f).WithReferenceDistance(radius)
                .WithMaxDistance(radius + SharedAudioSystem.DefaultSoundRange).WithVariation(0.025f));
        if (sound is { } stream)
        {
            state.ActiveImpactSound = stream.Entity;
            stream.Component.Flags |= AudioFlags.GridAudio | AudioFlags.NoOcclusion;
            _pvsSys.AddGlobalOverride(stream.Entity);
            Dirty(stream.Entity, stream.Component);
        }
    }
}