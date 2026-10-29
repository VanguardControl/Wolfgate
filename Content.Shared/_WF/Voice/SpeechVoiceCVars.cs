using Robust.Shared.Configuration;

namespace Content.Shared._WF.Voice;

/// <summary>Settings for voices.</summary>
[CVarDefs]
public sealed class SpeechVoiceCVars
{
    /// <summary>Plays other characters' voices. The creator's Test button plays either way.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.voice.enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
