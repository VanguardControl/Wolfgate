using Content.Server.Radio;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WG.Underground;

/// <summary>
/// The Underground's ship jammer: used in hand aboard a ship, it stops every radio message sent from that grid, NanoChat
/// included, until it burns out and is gone. Radio that can't be jammed still gets through.
/// </summary>
public sealed class WGShipJammerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipJammerComponent, UseInHandEvent>(OnUse);
        SubscribeLocalEvent<WGShipJammerComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<RadioSendAttemptEvent>(OnRadioSend);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGShipJammerComponent>();
        while (query.MoveNext(out var uid, out var jammer))
        {
            if (jammer.Until is not { } until || now < until)
                continue;

            _popup.PopupCoordinates(Loc.GetString("wg-ship-jammer-spent"), Transform(uid).Coordinates, PopupType.Medium);
            QueueDel(uid);
        }
    }

    private void OnUse(Entity<WGShipJammerComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (ent.Comp.Until != null)
        {
            _popup.PopupEntity(Loc.GetString("wg-ship-jammer-already"), ent, args.User);
            return;
        }

        if (Transform(args.User).GridUid == null)
        {
            _popup.PopupEntity(Loc.GetString("wg-ship-jammer-no-grid"), ent, args.User);
            return;
        }

        ent.Comp.Until = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.Seconds);
        _popup.PopupEntity(Loc.GetString("wg-ship-jammer-on", ("minutes", (int) Math.Ceiling(ent.Comp.Seconds / 60))), ent, args.User);
    }

    private void OnExamined(Entity<WGShipJammerComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Until is not { } until)
            return;

        var minutes = (int) Math.Ceiling(Math.Max(0, (until - _timing.CurTime).TotalMinutes));
        args.PushMarkup(Loc.GetString("wg-ship-jammer-examine", ("minutes", minutes)));
    }

    /// <summary>Whether a running jammer sits on this grid.</summary>
    public bool Jammed(EntityUid grid)
    {
        var query = EntityQueryEnumerator<WGShipJammerComponent, TransformComponent>();
        while (query.MoveNext(out _, out var jammer, out var xform))
        {
            if (jammer.Until != null && xform.GridUid == grid)
                return true;
        }

        return false;
    }

    private void OnRadioSend(ref RadioSendAttemptEvent args)
    {
        if (args.Cancelled || !args.Jammable || Transform(args.RadioSource).GridUid is not { } grid)
            return;

        if (Jammed(grid))
            args.Cancelled = true;
    }
}
