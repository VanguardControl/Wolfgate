using System.Numerics;
using Content.Server._WF.ShipShields;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    [Dependency] private IGameTiming _wfShieldTiming = default!;
    [Dependency] private IRobustRandom _wfShieldRandom = default!;
    private static readonly SoundCollectionSpecifier WolfgateImpactSound = new("WFShipShieldImpacts");

    /// <summary>Spaces impact sounds apart while placing each echo at its hull contact.</summary>
    private void PlayWolfgateShieldImpact(EntityUid shield, Vector2 position)
    {
        var state = EnsureComp<WFShipShieldImpactAudioComponent>(shield);
        var now = _wfShieldTiming.CurTime;
        if (now < state.NextImpactSound)
            return;
        state.NextImpactSound = now + TimeSpan.FromSeconds(_wfShieldRandom.NextFloat(2.4f, 3.6f));
        var contact = _transformSystem.ToMapCoordinates(new EntityCoordinates(shield, position));
        var sound = _audio.PlayPvs(WolfgateImpactSound, _transformSystem.ToCoordinates(contact),
            AudioParams.Default.WithVolume(-4f).WithReferenceDistance(8f).WithMaxDistance(60f).WithVariation(0.025f));
        if (sound is { } stream)
        {
            stream.Component.Flags |= AudioFlags.NoOcclusion;
            Dirty(stream.Entity, stream.Component);
        }
    }
}
