using Content.Server.Chat.Systems;
using Content.Server._WF.ShipPa;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server._WF.LockOn;

public sealed partial class WFLockOnSystem
{
    /// <summary>Least time between two lock warnings on the same ship.</summary>
    public static readonly TimeSpan AlertCooldown = TimeSpan.FromSeconds(10);

    /// <summary>To be replaced with a dedicated lock warning; the console's existing alarm is a placeholder.</summary>
    private static readonly SoundPathSpecifier LockWarning = new("/Audio/_WF/CombatConsole/Retro/warning.ogg");

    private static readonly Color LockColor = Color.FromHex("#ff3030");

    /// <summary>
    /// Warns a ship that something has a firing solution on it, through its PA when it has working speakers and
    /// in its crew's chat when it does not.
    /// </summary>
    private void Alert(EntityUid targetGrid, EntityUid lockerGrid)
    {
        if (targetGrid == lockerGrid || TerminatingOrDeleted(targetGrid) || TerminatingOrDeleted(lockerGrid))
            return;

        var alert = EnsureComp<WFLockOnAlertComponent>(targetGrid);
        if (alert.NextAlert > _timing.CurTime)
            return;
        alert.NextAlert = _timing.CurTime + AlertCooldown;

        var message = Loc.GetString("wf-lockon-alert", ("ship", _pa.GetShipName(lockerGrid)));
        var sender = Loc.GetString("wf-lockon-alert-sender");
        if (!_pa.Announce(targetGrid, message, LockWarning, sender, LockColor))
        {
            var crew = Filter.Empty().AddWhereAttachedEntity(entity => Transform(entity).GridUid == targetGrid);
            // The chat helper stringifies a sound specifier and hands that to the audio system, so the
            // fallback plays its own sound and leaves the announcement silent.
            _chat.DispatchFilteredAnnouncement(crew, message, source: targetGrid, sender: sender,
                playSound: false, announcementSound: null, colorOverride: LockColor);
            _audio.PlayGlobal(new ResolvedPathSpecifier(LockWarning.Path), crew, true,
                AudioParams.Default.WithVolume(-2f));
        }

        var ev = new WFLockOnAlertEvent(targetGrid, lockerGrid);
        RaiseLocalEvent(ref ev);
    }
}

/// <summary>Raised when a ship is warned that another ship has locked it.</summary>
[ByRefEvent]
public readonly record struct WFLockOnAlertEvent(EntityUid TargetGrid, EntityUid LockerGrid);
