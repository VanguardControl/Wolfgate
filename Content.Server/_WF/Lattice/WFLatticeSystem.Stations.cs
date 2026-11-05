using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server._WF.Defcon;
using Content.Server._WF.ShipShields;
using Content.Server.Chat.Systems;
using Content.Server.Power.Components;
using Content.Shared._WF.Announcements;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Ward;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// The primary anchors as small stations: a deck, the anchor core at its centre and a fortress shield. Drones still loyal
/// to the Lattice come out when one is attacked, and the loss of one is a breach announced to the whole sector. Relay
/// anchors that players build take hold once powered and parked.
/// </summary>
public sealed partial class WFLatticeSystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private MetaDataSystem _meta = default!;

    /// <summary>Furthest a station is pushed out along its bearing to find clear space, in metres.</summary>
    private const float StationNudge = 600f;

    private const float StationNudgeStep = 50f;

    private static readonly SoundSpecifier BreachSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    private List<Entity<MapGridComponent>> _stationGrids = new();

    private void InitializeStations()
    {
        SubscribeLocalEvent<WFLatticeAnchorComponent, DestructionEventArgs>(OnAnchorDestroyed);
        SubscribeLocalEvent<WFLatticeAnchorComponent, DamageChangedEvent>(OnAnchorDamaged);
        SubscribeLocalEvent<WFShipShieldAttackedEvent>(OnShieldAttacked);
        SubscribeLocalEvent<WFDefconGatherEvent>(OnDefconGather);
    }

    /// <summary>Each lost primary anchor raises the sector's DEFCON score.</summary>
    private void OnDefconGather(WFDefconGatherEvent ev)
    {
        var fields = EntityQueryEnumerator<WFLatticeFieldComponent>();
        while (fields.MoveNext(out _, out var field))
        {
            if (!_proto.TryIndex(field.Profile, out var profile))
                continue;

            var lost = (int) MathF.Round((1f - field.Integrity) * profile.AnchorCount);
            if (lost > 0)
                ev.Add(lost * profile.BreachDefcon, Loc.GetString("wf-lattice-defcon-reason", ("count", lost)));
        }
    }

    /// <summary>Builds one primary anchor's station at a corner of the hexagon, pushed outward clear of other grids.</summary>
    private void BuildStation(EntityUid map, WFLatticeProfilePrototype profile, Vector2 corner, int index)
    {
        var mapId = Transform(map).MapID;
        var bearing = corner.LengthSquared() > 0f ? Vector2.Normalize(corner) : Vector2.UnitX;
        var size = profile.StationRadius * 2f + 4f;
        var position = corner;
        for (var push = 0f; push <= StationNudge; push += StationNudgeStep)
        {
            position = corner + bearing * push;
            _stationGrids.Clear();
            _maps.FindGridsIntersecting(mapId, Box2.CenteredAround(position, new Vector2(size)), ref _stationGrids, true, false);
            if (_stationGrids.Count == 0)
                break;
        }

        var grid = _maps.CreateGridEntity(mapId);
        var tile = new Tile(_tiles[profile.StationTile].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        var radius = profile.StationRadius;
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (x * x + y * y <= radius * radius + radius)
                    tiles.Add((new Vector2i(x, y), tile));
            }
        }

        _maps.SetTiles(grid.Owner, grid.Comp, tiles);
        _transform.SetWorldPosition(grid.Owner, position);
        _meta.SetEntityName(grid.Owner, Loc.GetString("wf-lattice-station-name", ("anchor", AnchorName(index))));
        EnsureComp<CleanupImmuneComponent>(grid.Owner);

        var core = Spawn(profile.CorePrototypes[index % profile.CorePrototypes.Count], new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
        _meta.SetEntityName(core, AnchorName(index));
        var anchor = EnsureComp<WFLatticeAnchorComponent>(core);
        anchor.Primary = true;

        var shield = Spawn(profile.ShieldPrototype, new EntityCoordinates(grid.Owner, new Vector2(0.5f, -2.5f)));
        if (TryComp<ApcPowerReceiverComponent>(shield, out var power))
            power.NeedsPower = false;

        var station = EnsureComp<WFLatticeAnchorStationComponent>(grid.Owner);
        station.Core = core;
    }

    /// <summary>Alpha to Zeta, then numbers.</summary>
    private string AnchorName(int index)
    {
        return Loc.TryGetString($"wf-lattice-anchor-name-{index + 1}", out var name)
            ? name
            : Loc.GetString("wf-lattice-anchor-name-n", ("n", index + 1));
    }

    private void OnAnchorDestroyed(Entity<WFLatticeAnchorComponent> ent, ref DestructionEventArgs args)
    {
        if (!ent.Comp.Primary)
            return;

        // The anchor stops holding at once, before it is deleted.
        ent.Comp.Active = false;
        var bearing = _transform.GetWorldPosition(ent.Owner);
        var text = Loc.GetString("wf-lattice-breach", ("anchor", Name(ent.Owner)), ("direction", Compass(bearing)));
        _chat.DispatchGlobalAnnouncement(text, Loc.GetString("wf-lattice-chip-sender"), true, BreachSound, Color.FromHex("#d030a0"));
        RaiseNetworkEvent(new WFAnnouncementChipEvent
        {
            Level = WFAnnounceChipLevel.Warning,
            Accent = Color.FromHex("#d030a0"),
            Title = Loc.GetString("wf-lattice-breach-title"),
            Text = text,
            Sender = Loc.GetString("wf-lattice-chip-sender"),
            Duration = 30f,
            Flash = true,
        });
    }

    private void OnAnchorDamaged(Entity<WFLatticeAnchorComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.Primary && args.DamageIncreased && Transform(ent.Owner).GridUid is { } grid)
            Defend(grid, args.Origin is { } origin ? Transform(origin).GridUid : null);
    }

    private void OnShieldAttacked(ref WFShipShieldAttackedEvent args)
    {
        if (HasComp<WFLatticeAnchorStationComponent>(args.Grid))
            Defend(args.Grid, args.AttackerGrid);
    }

    /// <summary>Calls out the anchor's drones, between it and whoever attacked it, unless they came out lately.</summary>
    public void Defend(EntityUid station, EntityUid? attacker)
    {
        if (!TryComp<WFLatticeAnchorStationComponent>(station, out var comp)
            || _timing.CurTime < comp.NextDefence
            || Transform(station).MapUid is not { } map
            || !TryComp<WFLatticeFieldComponent>(map, out var field)
            || !_proto.TryIndex(field.Profile, out var profile))
            return;

        comp.NextDefence = _timing.CurTime + TimeSpan.FromSeconds(profile.DefenceCooldown);
        var from = _transform.GetWorldPosition(station);
        var towards = attacker is { } enemy && Exists(enemy)
            ? _transform.GetWorldPosition(enemy) - from
            : _random.NextAngle().ToVec();
        if (towards.LengthSquared() < 1f)
            towards = Vector2.UnitX;

        var point = from + Vector2.Normalize(towards) * profile.DefenceDistance;
        Spawn(profile.DefenderPrototype, new MapCoordinates(point, Transform(map).MapID));
    }

    /// <summary>Draws each anchor shield's field as a rooted Ward in the profile's tint; a field comes back new after a collapse.</summary>
    private void MarkShields()
    {
        var shields = EntityQueryEnumerator<WFLatticeAnchorShieldComponent, ShipShieldEmitterComponent, TransformComponent>();
        while (shields.MoveNext(out _, out _, out var emitter, out var xform))
        {
            if (emitter.Shield is not { } field
                || HasComp<WFWardFieldComponent>(field)
                || xform.MapUid is not { } map
                || !TryComp<WFLatticeFieldComponent>(map, out var lattice)
                || !_proto.TryIndex(lattice.Profile, out var profile))
                continue;

            var ward = EnsureComp<WFWardFieldComponent>(field);
            ward.Stage = WFMonolithStage.Crescendo;
            ward.Rooted = true;
            ward.Tint = profile.ShieldTint;
            Dirty(field, ward);
        }
    }

    /// <summary>A relay holds the field only while anchored, powered and on a grid that has sat still for a while.</summary>
    private void UpdateRelays(float seconds)
    {
        var relays = EntityQueryEnumerator<WFLatticeRelayComponent, WFLatticeAnchorComponent, TransformComponent>();
        while (relays.MoveNext(out var uid, out var relay, out var anchor, out var xform))
        {
            var still = xform.Anchored
                        && xform.GridUid is { } grid
                        && (!TryComp<PhysicsComponent>(grid, out var body) || body.LinearVelocity.Length() <= relay.MaxSpeed);
            var powered = !TryComp<ApcPowerReceiverComponent>(uid, out var power) || power.Powered;
            relay.Settled = still ? relay.Settled + seconds : 0f;
            anchor.Strength = relay.Strength;
            anchor.Active = powered && relay.Settled >= relay.SettleSeconds;
        }
    }

    /// <summary>Which way from the centre a point lies, as a compass word.</summary>
    private string Compass(Vector2 offset)
    {
        var degrees = MathF.Atan2(offset.Y, offset.X) * 180f / MathF.PI;
        // East is 0 and north 90; eight sectors of 45 degrees each, starting from east.
        var sector = (int) MathF.Round(((degrees % 360f) + 360f) % 360f / 45f) % 8;
        return Loc.GetString($"wf-lattice-compass-{sector}");
    }
}
