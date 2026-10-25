using Content.Shared._WF.CCVar;
using Content.Shared.Audio.Jukebox;
using Robust.Client.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;

namespace Content.Client._WF.JukeboxVolume;

/// <summary>Scales jukebox songs by the client's jukebox volume setting.</summary>
public sealed partial class JukeboxVolumeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private float _volume = 1f;

    public override void Initialize()
    {
        base.Initialize();

        // The engine resets each stream's gain every frame, so scaling after it never compounds.
        UpdatesAfter.Add(typeof(AudioSystem));

        Subs.CVar(_cfg, JukeboxVolumeCVars.Volume, value => _volume = value, true);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_volume >= 1f)
            return;

        var query = EntityQueryEnumerator<JukeboxComponent>();
        while (query.MoveNext(out _, out var jukebox))
        {
            if (TryComp<AudioComponent>(jukebox.AudioStream, out var audio))
                audio.Gain *= _volume;
        }
    }
}
