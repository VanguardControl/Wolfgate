using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared._WF.Wolfmed.Autodoc;
using Robust.Shared.Audio;
using Robust.Shared.Random;

namespace Content.Server._WF.Wolfmed.Autodoc;

public sealed partial class AutodocSystem
{
    /// <summary>
    /// One event, one line: the pod picks a variant, plays its ogg and says the matching transcript in chat.
    /// The transcript and the audio come from the same prototype row, so they can never disagree.
    /// </summary>
    public void Speak(Entity<AutodocComponent> ent, AutodocVoiceEvent voiceEvent, params (string, object)[] args)
    {
        if (!_protos.TryIndex(ent.Comp.Voice, out var voice) ||
            !voice.Events.TryGetValue(voiceEvent, out var ids) ||
            ids.Count == 0 ||
            !voice.Lines.TryGetValue(_random.Pick(ids), out var line))
            return;

        _audio.PlayPvs(line.Sound, ent.Owner, AudioParams.Default.WithVolume(ent.Comp.VoiceGain));
        _chat.TrySendInGameICMessage(ent.Owner, Loc.GetString(line.Message, args), InGameICChatType.Speak, false);
    }

    /// <summary>Small talk while there is somebody in the pod and nothing to do.</summary>
    private void TickIdleChatter(Entity<AutodocComponent> ent)
    {
        if (ent.Comp.State is not (AutodocState.Idle or AutodocState.Complete) ||
            !IsPowered(ent) ||
            GetOccupant(ent) == null)
            return;

        if (ent.Comp.NextIdleChatter == TimeSpan.Zero)
        {
            ent.Comp.NextIdleChatter = _timing.CurTime + TimeSpan.FromSeconds(
                _random.NextFloat(ent.Comp.IdleChatterMin, ent.Comp.IdleChatterMax));
            return;
        }

        if (_timing.CurTime < ent.Comp.NextIdleChatter)
            return;

        ent.Comp.NextIdleChatter = _timing.CurTime + TimeSpan.FromSeconds(
            _random.NextFloat(ent.Comp.IdleChatterMin, ent.Comp.IdleChatterMax));
        Speak(ent, IsEmagged(ent) ? AutodocVoiceEvent.Emag : AutodocVoiceEvent.Idle);
    }
}
