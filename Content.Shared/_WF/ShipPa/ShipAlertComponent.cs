using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ShipPa;

/// <summary>
/// A ship's situation code and alarm state. Lives on the grid entity and is ensured by the server
/// the moment the grid has a speaker on it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipAlertComponent : Component
{
    [DataField, AutoNetworkedField] public ProtoId<ShipAlertCodePrototype> Code = "ShipCodeGreen";

    [DataField, AutoNetworkedField] public bool GeneralQuarters;

    [DataField, AutoNetworkedField] public int SpeakersOnline;

    [DataField, AutoNetworkedField] public int SpeakersTotal;

    /// <summary>True while the counts above are the ship's air alarms standing in for speakers.</summary>
    [DataField, AutoNetworkedField] public bool SpeakersFallback;

    /// <summary>Looped on every speaker while general quarters is sounded.</summary>
    [DataField] public SoundSpecifier GeneralQuartersAlarm = new SoundPathSpecifier("/Audio/Misc/redalert.ogg");

    [DataField] public LocId GeneralQuartersAnnouncement = "ship-pa-general-quarters-announcement";

    [DataField] public LocId GeneralQuartersSecureAnnouncement = "ship-pa-general-quarters-secure";

    /// <summary>Colour of the general quarters text over the speakers and in chat.</summary>
    [DataField] public Color GeneralQuartersColor = Color.FromHex("#ff3030");

    /// <summary>Played through the speakers when general quarters is sounded; the chime when unset.</summary>
    [DataField] public SoundSpecifier? GeneralQuartersSound;

    /// <summary>Played when securing from general quarters; the chime when unset.</summary>
    [DataField] public SoundSpecifier? GeneralQuartersSecureSound;

    /// <summary>Played before free-text announcements.</summary>
    [DataField] public SoundSpecifier AnnouncementChime = new SoundPathSpecifier("/Audio/Announcements/attention.ogg");

    [DataField] public TimeSpan AnnouncementCooldown = TimeSpan.FromSeconds(5);

    [DataField] public int MaxAnnouncementLength = 256;

    /// <summary>Server only.</summary>
    [ViewVariables] public TimeSpan NextAnnouncement;

    /// <summary>Longest link the console will take, so a pasted wall of text never reaches yt-dlp.</summary>
    [DataField] public int MaxUrlLength = 512;

    /// <summary>Server only. When this ship may next queue an internet sound from its console.</summary>
    [ViewVariables] public TimeSpan NextInternetSound;
}
