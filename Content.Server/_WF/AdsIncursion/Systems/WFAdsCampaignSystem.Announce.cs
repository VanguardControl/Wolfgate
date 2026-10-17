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

    /// <summary>A sector-wide announcement from the sector watch: purple with the rising sound, or teal with the falling one.</summary>
    private void Announce(WFAdsCampaignPrototype campaign, string message, bool rising, bool chime = true)
    {
        _chat.DispatchGlobalAnnouncement(message, Loc.GetString("wf-ads-watch"), playSound: chime,
            announcementSound: chime ? rising ? campaign.RisingSound : campaign.FallingSound : null,
            colorOverride: rising ? campaign.RisingColor : campaign.FallingColor);
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

    /// <summary>Says something on the common channel as the sector watch.</summary>
    private void Radio(MapId map, string message)
    {
        if (State() is not { } state || Watch(state, map) is not { } watch)
            return;

        _radio.SendRadioMessage(watch, message, WarningChannel, watch);
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
