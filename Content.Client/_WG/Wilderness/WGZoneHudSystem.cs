using System.Linq;
using Content.Client._WG.Announcements;
using Content.Client.Guidebook.Controls;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Client.UserInterface.Systems.Info;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared._WG.Wilderness;
using Robust.Client.Audio;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WG.Wilderness;

/// <summary>
/// Shows the local player the zone they're in and their Wilderness mark: a pill at the top of the screen, a title on
/// crossing in or out, a coloured screen border while marked, and a skull over marked people's heads for everyone.
/// Each part follows its own setting in the Extra options tab; by default only danger gets more than the alert.
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

    private static readonly Color MarkColor = Color.FromHex("#ff3b30");
    private static readonly SoundSpecifier MarkedSound = new SoundPathSpecifier("/Audio/Effects/alert.ogg");
    private const string DefaultGuide = "WGWilderness";
    private static readonly SpriteSpecifier MarkedIcon = newSpriteSpecifier.Rsi(new ResPath("/Textures/_WG/Wilderness/zones.rsi"), "marked");

    private const float TitleHold = 1.5f;
    private const float DetailsOpen = 5f;
    private const float TitleFade = 0.6f;
    private const float BannerTop = 8f;
    private const float ChipGap = 8f;

    /// <summary>Decibels taken off every zone sound.</summary>
    private const float SoundQuieter = -6f;

    private WGZoneBanner? _banner;
    private readonly WGZoneBorderOverlay _border = new();
    private WGZoneTitleOverlay? _title;
    private WGSkullOverlay? _skulls;

    private float _titleAge = float.MaxValue;
    private bool _seen;
    private string? _lastZone;
    private string? _lastPlace;
    private bool _lastMarked;
    private bool _lastDanger;

    /// <summary>Seconds the banner's details stay open after entering danger.</summary>
    private float _openFor;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Forget());
        _skulls = new WGSkullOverlay();
        _overlays.AddOverlay(_skulls);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _banner?.Orphan();
        _banner = null;
        _overlays.RemoveOverlay(_border);
        if (_title != null)
            _overlays.RemoveOverlay(_title);
        if (_skulls != null)
            _overlays.RemoveOverlay(_skulls);
    }

    private void Forget()
    {
        _seen = false;
        _lastZone = null;
        _lastPlace = null;
        _lastMarked = false;
        _lastDanger = false;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity is not { } mob)
        {
            HideBanner();
            HideBorder();
            ClearTitle();
            return;
        }

        TryComp<WGInZoneComponent>(mob, out var inZone);
        WGZonePrototype? zone = null;
        if (inZone != null)
            _proto.TryIndex(inZone.Zone, out zone);

        // The player's own mark first, else the mark of the ship they're aboard.
        var vessel = Vessel(mob);
        var ship = false;
        TryComp<WGSkullComponent>(mob, out var skull);
        if (skull == null && vessel is { } aboard && TryComp(aboard, out skull))
            ship = HasComp<MapGridComponent>(aboard);

        WGWildernessDwellComponent? dwell = null;
        if (skull == null && !TryComp(mob, out dwell) && vessel is { } staying && TryComp(staying, out dwell))
            ship = HasComp<MapGridComponent>(staying);

        var marked = skull != null;
        var danger = Danger(zone, inZone, marked);
        Announce(zone, inZone, marked, danger);
        UpdateBanner(zone, inZone, skull, dwell, ship, danger, frameTime);
        UpdateBorder(zone, marked);
        UpdateTitle(frameTime);
    }

    /// <summary>Where the player can be attacked without escalation: a Wilderness, an outpost at war, or anywhere while marked.</summary>
    private static bool Danger(WGZonePrototype? zone, WGInZoneComponent? inZone, bool marked)
    {
        return marked
            || zone?.Kind == WGZoneKind.Wilderness
            || (zone?.Kind == WGZoneKind.Outpost && inZone?.Hot == true);
    }

    /// <summary>Whether an always/danger/off setting lets something show, given whether the player is in danger.</summary>
    private bool Allows(CVarDef<string> setting, bool danger)
    {
        return _cfg.GetCVar(setting) switch
        {
            WildernessCVars.ShowAlways => true,
            WildernessCVars.ShowOff => false,
            _ => danger,
        };
    }

    /// <summary>What the player is aboard that carries a mark of its own: a ship marked or being marked, or a vehicle.</summary>
    private EntityUid? Vessel(EntityUid mob)
    {
        var xform = Transform(mob);
        EntityUid? vehicle = null;
        if (TryComp<VehicleInteriorLinkComponent>(xform.MapUid, out var link) && link.Vehicle != mob && Exists(link.Vehicle))
        {
            vehicle = link.Vehicle;
            xform = Transform(link.Vehicle);
        }

        if (xform.GridUid is { } grid && (HasComp<WGSkullComponent>(grid) || HasComp<WGWildernessDwellComponent>(grid)))
            return grid;

        return vehicle;
    }

    /// <summary>The title and sound on crossing into or out of a zone, and on being marked, as the settings allow.</summary>
    private void Announce(WGZonePrototype? zone, WGInZoneComponent? inZone, bool marked, bool danger)
    {
        var id = zone?.ID;
        var place = zone != null ? inZone?.Place : null;
        if (!_seen)
        {
            _seen = true;
            _lastMarked = marked;
            _lastZone = null;
            _lastPlace = null;
            _lastDanger = false;
        }

        if (marked && !_lastMarked)
        {
            if (Allows(WildernessCVars.Titles, true))
                ShowTitle(Loc.GetString("wg-zone-title-marked"), Loc.GetString("wg-zone-title-marked-sub"), MarkColor);
            if (Allows(WildernessCVars.Sounds, true))
                Play(MarkedSound);
            _openFor = DetailsOpen;
        }
        else if (id != _lastZone || place != _lastPlace)
        {
            if (zone != null && inZone != null)
            {
                if (Allows(WildernessCVars.Titles, danger))
                {
                    var title = Loc.GetString("wg-zone-title-entering", ("title", WGZoneText.Title(zone, inZone.Hot)));
                    ShowTitle(title, inZone.Place, zone.Color);
                }

                if (zone.EnterSound != null && Allows(WildernessCVars.Sounds, danger))
                    Play(zone.EnterSound);

                if (danger)
                    _openFor = DetailsOpen;
            }
            else if (_lastZone != null && _proto.TryIndex<WGZonePrototype>(_lastZone, out var old) && Allows(WildernessCVars.Titles, _lastDanger))
            {
                ShowTitle(Loc.GetString("wg-zone-title-leaving", ("title", Loc.GetString(old.Title))), string.Empty, Color.FromHex("#c8ccd0"));
            }
        }

        _lastZone = id;
        _lastPlace = place;
        _lastMarked = marked;
        _lastDanger = danger;
    }

    private void Play(SoundSpecifier sound)
    {
        _audio.PlayGlobal(sound, Filter.Local(), false, sound.Params.AddVolume(SoundQuieter));
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
        bool ship, bool danger, float frameTime)
    {
        _openFor = MathF.Max(0f, _openFor - frameTime);
        if ((zone == null && skull == null) || !Allows(WildernessCVars.Banner, danger))
        {
            HideBanner();
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

        banner.HoldOpen = _openFor > 0f;

        var open = _cfg.GetCVar(WildernessCVars.OpenToAll);
        if (zone != null && inZone != null)
        {
            var summary = WGZoneText.Summary(zone, inZone.Hot, open, _cfg.GetCVar(WildernessCVars.SkullOnFoot));
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

        banner.Visible = true;

        // Under the top menu, so it never covers its buttons.
        var top = BannerTop;
        if (_ui.ActiveScreen?.GetWidget<GameTopMenuBar>() is { Visible: true } bar)
            top = MathF.Max(top, bar.GlobalPosition.Y + bar.Size.Y + BannerTop);

        LayoutContainer.SetMarginTop(banner, top);
        _chips.TopMargin = top + banner.Size.Y + ChipGap;
    }

    private void UpdateBorder(WGZonePrototype? zone, bool marked)
    {
        var show = _cfg.GetCVar(WildernessCVars.ScreenBorder) switch
        {
            WildernessCVars.BorderWilderness => marked || zone?.Border == true,
            WildernessCVars.ShowOff => false,
            _ => marked,
        };

        if (!show)
        {
            HideBorder();
            return;
        }

        if (!_overlays.HasOverlay<WGZoneBorderOverlay>())
            _overlays.AddOverlay(_border);

        _border.Color = marked ? MarkColor : zone!.Color;
        _border.Alpha = 1f;
    }

    private void HideBanner()
    {
        if (_banner != null)
            _banner.Visible = false;

        _chips.TopMargin = null;
    }

    private void HideBorder()
    {
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

    /// <summary>
    /// Opens the zone's rule. Rule pages aren't in the guidebook's tree, so it opens on the core rules and then follows
    /// a link to the page, as a link inside the rules would.
    /// </summary>
    private void OpenRule()
    {
        if (_player.LocalEntity is not { } mob
            || !TryComp<WGInZoneComponent>(mob, out var inZone)
            || !_proto.TryIndex(inZone.Zone, out var zone)
            || zone.Guide is not { } guide)
        {
            guide = DefaultGuide;
        }

        var rules = _ui.GetUIController<InfoUIController>().GetCoreRuleEntry().ID;
        _ui.GetUIController<GuidebookUIController>().OpenGuidebook(selected: rules);
        _ui.WindowRoot.Children.OfType<GuidebookWindow>().FirstOrDefault()?.HandleClick(guide.Id);
    }
}
