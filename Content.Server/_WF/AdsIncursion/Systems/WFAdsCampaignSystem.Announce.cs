using System.Numerics;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Shared._WF.AdsIncursion;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.AdsIncursion.Systems;

public sealed partial class WFAdsCampaignSystem
{
    private static readonly ProtoId<RadioChannelPrototype> WarningChannel = "Common";

    /// <summary>How long a quiet banner holds the next one back; a quiet banner inside it goes out on the radio instead.</summary>
    public static readonly TimeSpan QuietBannerInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A banner to everyone: purple with the rising sound, or teal with the falling one; without <paramref name="chime"/> it is silent and
    /// never held back. A chimed rising banner is the system's own anomaly alert, with the malfunctioning-AI klaxon, not the sector watch.
    /// </summary>
    private void Announce(WFAdsCampaignPrototype campaign, string message, bool rising, bool chime = true)
    {
        var sender = chime && rising ? "wf-ads-anomaly-alert" : "wf-ads-watch";
        _chat.DispatchGlobalAnnouncement(message, Loc.GetString(sender), playSound: chime,
            announcementSound: chime ? rising ? campaign.RisingSound : campaign.FallingSound : null,
            colorOverride: rising ? campaign.RisingColor : campaign.FallingColor);
    }

    /// <summary>A banner to everyone with no sound, at most one per <see cref="QuietBannerInterval"/>; inside the window it goes out on the radio.</summary>
    private void AnnounceQuiet(WFAdsCampaignPrototype campaign, string message, bool rising)
    {
        var now = _timing.CurTime;
        if (State() is { } state)
        {
            if (state.LastQuietBanner is { } last && now - last < QuietBannerInterval && Radio(_territory.SectorMap, message))
                return;

            state.LastQuietBanner = now;
        }

        _chat.DispatchGlobalAnnouncement(message, Loc.GetString("wf-ads-watch"), playSound: false,
            colorOverride: rising ? campaign.RisingColor : campaign.FallingColor);
    }

    /// <summary>A routine line on the common channel from the sector watch, with no banner.</summary>
    private void AnnounceRadio(string message)
    {
        Radio(_territory.SectorMap, message);
    }

    /// <summary>Announces something at a place, with the station, bearing and range from the nearest station added as arguments.</summary>
    private void AnnouncePlace(WFAdsCampaignPrototype campaign, string key, bool rising, MapId map, Vector2 position,
        params (string, object)[] args)
    {
        var (station, bearing, range) = Place(map, position);
        var all = new List<(string, object)>(args) { ("station", station), ("bearing", bearing), ("range", range) };
        var chime = State()?.TierAnnounced != _timing.CurTime;
        Announce(campaign, Loc.GetString(key, all.ToArray()), rising, chime);
    }

    /// <summary>The nearest station to a point, with the bearing and range from it to the point; the origin if there is none.</summary>
    private (string Station, string Bearing, string Range) Place(MapId map, Vector2 position)
    {
        string? name = null;
        var from = Vector2.Zero;
        var best = float.MaxValue;
        foreach (var station in _territory.Stations(map))
        {
            var at = _transform.GetWorldPosition(station);
            var distance = Vector2.DistanceSquared(at, position);
            if (distance >= best)
                continue;

            best = distance;
            from = at;
            name = Name(station);
        }

        return (name ?? Loc.GetString("wf-ads-place-origin"),
            WFReaverCampaignSystem.Bearing(from, position),
            WFReaverCampaignSystem.Kilometres(from, position));
    }

    /// <summary>Warns a ship over its PA and in chat to those aboard, and the sector on the common channel, with the bearing and range from it to the ADS ship.</summary>
    private void WarnShip(WFAdsCampaignPrototype campaign, MapId map, EntityUid ship, Vector2 ads, string key)
    {
        var at = _transform.GetWorldPosition(ship);
        var message = Loc.GetString(key,
            ("ship", Name(ship)),
            ("bearing", WFReaverCampaignSystem.Bearing(at, ads)),
            ("range", WFReaverCampaignSystem.Kilometres(at, ads)));
        _notices.Tell(ship, message, Loc.GetString("wf-ads-watch"), campaign.RisingColor, true);
        Radio(map, message);
    }

    /// <summary>Says something on the common channel as the sector watch; false if there is no watch to say it.</summary>
    private bool Radio(MapId map, string message)
    {
        if (State() is not { } state || Watch(state, map) is not { } watch)
            return false;

        _radio.SendRadioMessage(watch, message, WarningChannel, watch);
        return true;
    }

    /// <summary>The sector watch's radio for the ADS: an entity on the map, named for it and heard without a telecom server.</summary>
    private EntityUid? Watch(WFAdsCampaignComponent state, MapId map)
    {
        if (state.Watch is { } existing && !TerminatingOrDeleted(existing))
            return existing;

        if (!_map.TryGetMap(map, out var mapUid) || mapUid is not { } uid)
            return null;

        var watch = Spawn(null, new EntityCoordinates(uid, Vector2.Zero));
        _meta.SetEntityName(watch, Loc.GetString("wf-ads-watch"));
        EnsureComp<TelecomExemptComponent>(watch);
        state.Watch = watch;
        return watch;
    }
}
