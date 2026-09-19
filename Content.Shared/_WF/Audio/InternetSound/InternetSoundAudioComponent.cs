using Robust.Shared.GameStates;

namespace Content.Shared._WF.Audio.InternetSound;

/// <summary>
/// Marks the audio entity of an internet sound played to everyone at once, so the client can hold it at
/// whatever the listener set on the radio. Ship PA tracks don't get this: they're diegetic and follow the
/// ordinary volume sliders like any other sound a speaker makes.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class InternetSoundAudioComponent : Component;
