using Content.Client._WG.Announcements;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared._WG.Wilderness;
using Content.Shared.CCVar;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Client.Audio;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WG.Wilderness;

/// <summary>
/// Shows the local player the zone they're in and their Wilderness mark: a banner at the top of the screen, a large
/// title on crossing in or out, a coloured screen border in a Wilderness or while marked, and a skull over marked
/// people's heads for everyone.
/// </summary>
public sealed class WGZoneHudSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WGAnnouncementChipSystem _chips = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private static readonly ProtoId<FactionIconPrototype> SkullIcon = "WGSkullIcon";
    private static readonly Color MarkColor = Color.FromHex("#ff3b30");
    private static readonly SoundSpecifier MarkedSound = new SoundPathSpecifier("/Audio/Effects/alert.ogg");
    private static readonly SpriteSpecifier MarkedIcon = new SpriteSpecifier.Rsi(new ResPath("/Textures/_WG/Wilderness/zones.rsi"), "marked");

    private const float TitleHold = 2.5f;
    private const float DetailsOpen = 8f;
    private const float TitleFade = 0.8f;
    private const float BannerTop = 8f;
    private const float ChipGap = 8f;

    private WGZoneBanner? _banner;
    private readonly WGZoneBorderOverlay _border = new();
    private WGZoneTitleOverlay? _title;

    private float _titleAge = float.MaxValue;
    private bool _seen;
    private string? _lastZone;
    private bool _lastMarked;
    private float _pulse;

    /// <summary>Seconds the banner's details stay open after the zone or mark changes.</summary>
    private float _openFor;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSkullComponent, GetStatusIconsEvent>(OnStatusIcons);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Forget());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _banner?.Orphan();
        _banner = null;
        _overlays.RemoveOverlay(_border);
        if (_title != null)
            _overlays.RemoveOverlay(_title);
    }

    private void OnStatusIcons(Entity<WGSkullComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_proto.TryIndex(SkullIcon, out var icon))
            args.StatusIcons.Add(icon);
    }

    private void Forget()
    {
        _seen = false;
        _lastZone = null;
        _lastMarked = false;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity is not { } mob)
        {
            Hide();
            ClearTitle();
            return;
        }

        TryComp<WGInZoneComponent>(mob, out var inZone);
        WGZonePrototype? zone = null;
        if (inZone != null)
            _proto.TryIndex(inZone.Zone, out zone);

        var carrier = Carrier(mob, out var ship);
        TryComp<WGSkullComponent>(carrier, out var skull);
        TryComp<WGWildernessDwellComponent>(carrier, out var dwell);

        var reduced = _cfg.GetCVar(CCVars.ReducedMotion);
        _pulse += frameTime;
        var wave = reduced ? 1f : 0.65f + 0.35f * MathF.Abs(MathF.Sin(_pulse * 2.4f));

        Announce(zone, inZone, skull != null, ship);
        UpdateBanner(zone, inZone, skull, dwell, ship, wave, frameTime);
        UpdateBorder(zone, skull != null, wave);
        UpdateTitle(frameTime);
    }

    /// <summary>The ship the player is aboard, or the player: whatever would carry their mark.</summary>
    private EntityUid Carrier(EntityUid mob, out bool ship)
    {
        var xform = Transform(mob);
        if (TryComp<VehicleInteriorLinkComponent>(xform.MapUid, out var link) && link.Vehicle != mob && Exists(link.Vehicle))
        {
            mob = link.Vehicle;
            xform = Transform(mob);
        }

        if (xform.GridUid is { } grid && (HasComp<WGSkullComponent>(grid) || HasComp<WGWildernessDwellComponent>(grid)))
        {
            ship = true;
            return grid;
        }

        ship = false;
        return mob;
    }

    /// <summary>The title and sound on crossing into or out of a zone, and on being marked.</summary>
    private void Announce(WGZonePrototype? zone, WGInZoneComponent? inZone, bool marked, bool ship)
    {
        var id = zone?.ID;
        if (!_seen)
        {
            _seen = true;
            _lastMarked = marked;
            _lastZone = null;
        }

        if (marked && !_lastMarked)
        {
            ShowTitle(Loc.GetString("wg-zone-title-marked"), Loc.GetString("wg-zone-title-marked-sub"), MarkColor);
            _audio.PlayGlobal(MarkedSound, Filter.Local(), false);
        }
        else if (id != _lastZone)
        {
            if (zone != null && inZone != null)
            {
                var title = Loc.GetString("wg-zone-title-entering", ("title", WGZoneText.Title(zone, inZone.Hot)));
                ShowTitle(title, inZone.Place, zone.Color);
                if (zone.EnterSound != null)
                    _audio.PlayGlobal(zone.EnterSound, Filter.Local(), false);
            }
            else if (_lastZone != null && _proto.TryIndex<WGZonePrototype>(_lastZone, out var old))
            {
                ShowTitle(Loc.GetString("wg-zone-title-leaving", ("title", Loc.GetString(old.Title))), string.Empty, Color.FromHex("#c8ccd0"));
            }
        }

        _lastZone = id;
        _lastMarked = marked;
    }

    private void ShowTitle(string title, string sub, Color color)
    {
        if (_title == null)
        {
            _title = new WGZoneTitleOverlay();
            _overlays.AddOverlay(_title);
        }

        _title.Title = title;
        _title.Sub = sub;
        _title.Color = color;
        _titleAge = 0f;
        _openFor = DetailsOpen;
    }

    private void UpdateTitle(float frameTime)
    {
        if (_title == null)
            return;

        _titleAge += frameTime;
        _title.Alpha = _titleAge < TitleHold ? 1f : Math.Clamp(1f - (_titleAge - TitleHold) / TitleFade, 0f, 1f);
    }

    /// <summary>Takes the title down at once, as when there is no body to show it for.</summary>
    private void ClearTitle()
    {
        _titleAge = float.MaxValue;
        if (_title != null)
            _title.Alpha = 0f;
    }

    private void UpdateBanner(WGZonePrototype? zone, WGInZoneComponent? inZone, WGSkullComponent? skull, WGWildernessDwellComponent? dwell,
        bool ship, float wave, float frameTime)
    {
        if (zone == null && skull == null)
        {
            Hide();
            return;
        }

        var banner = EnsureBanner();
        var shipArg = ship ? "yes" : "no";
        string? mark = null;
        string? markDetail = null;
        if (skull != null)
        {
            if (skull.ExpiresAt is { } expires)
            {
                var time = WGZoneText.Clock(expires - _timing.CurTime);
                mark = Loc.GetString("wg-zone-pill-marked", ("time", time));
                markDetail = Loc.GetString("wg-zone-banner-marked", ("ship", shipArg), ("time", time));
            }
            else
            {
                mark = Loc.GetString("wg-zone-pill-marked-held");
                markDetail = Loc.GetString("wg-zone-banner-marked-held", ("ship", shipArg), ("minutes", _cfg.GetCVar(WildernessCVars.SkullMinutes)));
            }
        }
        else if (dwell != null)
        {
            var left = (int) MathF.Ceiling(MathF.Max(_cfg.GetCVar(WildernessCVars.DwellSeconds) - (float) (_timing.CurTime - dwell.Since).TotalSeconds, 0f));
            mark = Loc.GetString("wg-zone-pill-marking", ("seconds", left));
            markDetail = Loc.GetString("wg-zone-banner-marking", ("ship", shipArg), ("seconds", left));
        }

        _openFor = MathF.Max(0f, _openFor - frameTime);
        banner.HoldOpen = _openFor > 0f;

        var open = _cfg.GetCVar(WildernessCVars.OpenToAll);
        if (zone != null && inZone != null)
        {
            var summary = WGZoneText.Summary(zone, inZone.Hot, open);
            if (skull != null && zone.Kind == WGZoneKind.Safe)
                summary = Loc.GetString("wg-zone-banner-overridden");

            var color = skull != null && zone.Kind == WGZoneKind.Safe ? MarkColor : zone.Color;
            var title = WGZoneText.Title(zone, inZone.Hot);
            if (inZone.Place.Length > 0)
                title = Loc.GetString("wg-zone-banner-title", ("title", title), ("place", inZone.Place));

            banner.Set(color, zone.Icon != null ? _sprite.Frame0(zone.Icon) : null, title, summary, mark, markDetail,
                zone.Kind == WGZoneKind.Wilderness || skull != null);
        }
        else
        {
            banner.Set(MarkColor, _sprite.Frame0(MarkedIcon),
                Loc.GetString("wg-zone-title-marked"), Loc.GetString("wg-zone-title-marked-sub"), mark, markDetail, true);
        }

        var urgent = zone?.Kind == WGZoneKind.Wilderness || skull != null;
        banner.Pulse(urgent ? wave : 1f);
        banner.Visible = true;

        // Under the top menu, so it never covers its buttons.
        var top = BannerTop;
        if (_ui.ActiveScreen?.GetWidget<GameTopMenuBar>() is { Visible: true } bar)
            top = MathF.Max(top, bar.GlobalPosition.Y + bar.Size.Y + BannerTop);

        LayoutContainer.SetMarginTop(banner, top);
        _chips.TopMargin = top + banner.Size.Y + ChipGap;
    }
    private void UpdateBorder(WGZonePrototype? zone, bool marked, float wave)
    {
        var show = _cfg.GetCVar(WildernessCVars.Border) && (marked || zone?.Border == true);
        if (!show)
        {
            if (_overlays.HasOverlay<WGZoneBorderOverlay>())
                _overlays.RemoveOverlay(_border);
            return;
        }

        if (!_overlays.HasOverlay<WGZoneBorderOverlay>())
            _overlays.AddOverlay(_border);

        _border.Color = marked ? MarkColor : zone!.Color;
        _border.Alpha = wave;
    }

    private void Hide()
    {
        if (_banner != null)
            _banner.Visible = false;

        _chips.TopMargin = null;
        if (_overlays.HasOverlay<WGZoneBorderOverlay>())
            _overlays.RemoveOverlay(_border);
    }

    private WGZoneBanner EnsureBanner()
    {
        if (_banner is { Parent: not null })
            return _banner;

        _banner = new WGZoneBanner();
        _banner.Clicked += OpenRule;
        _ui.PopupRoot.AddChild(_banner);
        LayoutContainer.SetAnchorPreset(_banner, LayoutContainer.LayoutPreset.CenterTop);
        LayoutContainer.SetGrowHorizontal(_banner, LayoutContainer.GrowDirection.Both);
        LayoutContainer.SetMarginTop(_banner, BannerTop);
        return _banner;
    }

    private void OpenRule()
    {
        if (_player.LocalEntity is not { } mob
            || !TryComp<WGInZoneComponent>(mob, out var inZone)
            || !_proto.TryIndex(inZone.Zone, out var zone)
            || zone.Guide is not { } guide)
        {
            guide = "WGWilderness";
        }

        _ui.GetUIController<GuidebookUIController>().OpenGuidebook(selected: guide);
    }
}
