using System.Numerics;
using Content.Server._WF.Remade.Corruption;
using Content.Server.Light.EntitySystems;
using Content.Server.Station.Components;
using Content.Shared._WF.Announcements;
using Content.Shared._WF.Lattice;
using Content.Shared.Destructible;
using Content.Shared.Light.Components;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.Lattice;

/// <summary>
/// The end of the Lattice. When the last primary anchor falls the Unstringing comes: no anchor holds anything anywhere and
/// the Coda is everywhere. Some time later Kyphrus implodes (Tacet): every bulb in the sector bursts, and new ones keep
/// failing. Also places Caelestinus Central's own anchor, which keeps corruption off the station while it stands.
/// </summary>
public sealed partial class WFLatticeSystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private PoweredLightSystem _lights = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;

    private const string CentralPrototype = "WFLatticeCentralAnchor";
    private static readonly Color EndColor = Color.FromHex("#8a1a5a");
    private static readonly SoundSpecifier UnstringSound = new SoundPathSpecifier("/Audio/Misc/delta_alt.ogg");
    private static readonly SoundSpecifier TacetSound = new SoundPathSpecifier("/Audio/_WF/Lattice/drift_pulse.ogg");

    /// <summary>Maps whose central anchor has been placed or found.</summary>
    private readonly HashSet<EntityUid> _central = new();

    /// <summary>When each unstrung map goes to Tacet, and after it, when its bulbs are next rolled.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _tacetAt = new();

    private void InitializeEndgame()
    {
        SubscribeLocalEvent<WFLatticeBreachEvent>(OnBreach);
        SubscribeLocalEvent<WFLatticeCentralAnchorComponent, DestructionEventArgs>(OnCentralDestroyed);
    }

    private void OnBreach(ref WFLatticeBreachEvent args)
    {
        if (Transform(args.Anchor).MapUid is not { } map || !TryComp<WFLatticeFieldComponent>(map, out var field) || field.Unstrung)
            return;

        var query = EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var anchor, out var xform))
        {
            if (anchor.Primary && anchor.Active && xform.MapUid == map && !TerminatingOrDeleted(uid))
                return;
        }

        Unstring((map, field));
    }

    /// <summary>The Unstringing: nothing holds the map any more. Starts the clock on Tacet.</summary>
    public void Unstring(Entity<WFLatticeFieldComponent> map)
    {
        if (map.Comp.Unstrung)
            return;

        map.Comp.Unstrung = true;
        Dirty(map);
        GatherAnchors(map);
        SyncAnchors(map);
        SyncHex(map);
        Announce("wf-lattice-unstring", "wf-lattice-unstring-title", "wf-lattice-unstring-chip", UnstringSound);
        Log.Info($"The Lattice of {ToPrettyString(map)} is unstrung.");

        if (_config.GetCVar(WFLatticeEndgameCVars.Tacet))
        {
            var min = _config.GetCVar(WFLatticeEndgameCVars.TacetMin);
            var max = Math.Max(min, _config.GetCVar(WFLatticeEndgameCVars.TacetMax));
            _tacetAt[map] = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(min, max));
        }
    }

    /// <summary>Tacet: Kyphrus implodes and the sector goes dark.</summary>
    public void Tacet(Entity<WFLatticeFieldComponent> map)
    {
        if (map.Comp.Tacet)
            return;

        if (!map.Comp.Unstrung)
            Unstring(map);

        map.Comp.Tacet = true;
        Dirty(map);
        Announce("wf-lattice-tacet", "wf-lattice-tacet-title", "wf-lattice-tacet-chip", TacetSound);
        BurstBulbs(map, 1f);
        _tacetAt[map] = _timing.CurTime + TimeSpan.FromSeconds(_config.GetCVar(WFLatticeEndgameCVars.DarkInterval));
        Log.Info($"Tacet on {ToPrettyString(map)}.");
    }

    private void UpdateEndgame(Entity<WFLatticeFieldComponent> map)
    {
        if (!map.Comp.Unstrung || !_tacetAt.TryGetValue(map, out var at) || _timing.CurTime < at)
            return;

        if (!map.Comp.Tacet)
        {
            Tacet(map);
            return;
        }

        _tacetAt[map] = _timing.CurTime + TimeSpan.FromSeconds(_config.GetCVar(WFLatticeEndgameCVars.DarkInterval));
        BurstBulbs(map, _config.GetCVar(WFLatticeEndgameCVars.DarkChance));
    }

    /// <summary>Bursts each working bulb on the map with the given chance.</summary>
    private void BurstBulbs(EntityUid map, float chance)
    {
        var query = EntityQueryEnumerator<PoweredLightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var light, out var xform))
        {
            if (xform.MapUid == map && _random.Prob(chance))
                _lights.TryDestroyBulb(uid, light);
        }
    }

    private void Announce(string text, string title, string chip, SoundSpecifier sound)
    {
        _chat.DispatchGlobalAnnouncement(Loc.GetString(text), Loc.GetString("wf-lattice-chip-sender"), true, sound, EndColor);
        RaiseNetworkEvent(new WFAnnouncementChipEvent
        {
            Level = WFAnnounceChipLevel.Warning,
            Accent = EndColor,
            Title = Loc.GetString(title),
            Text = Loc.GetString(chip),
            Sender = Loc.GetString("wf-lattice-chip-sender"),
            Duration = 30f,
            Flash = true,
        });
    }

    /// <summary>Puts Caelestinus Central's anchor on the station once it exists, unless one is there already.</summary>
    private void PlaceCentral(EntityUid map)
    {
        if (_central.Contains(map))
            return;

        var stations = EntityQueryEnumerator<BecomesStationComponent, MapGridComponent, TransformComponent>();
        while (stations.MoveNext(out var grid, out _, out var gridComp, out var xform))
        {
            if (xform.MapUid != map || !_corruption.IsMainStation(grid))
                continue;

            _central.Add(map);
            var existing = EntityQueryEnumerator<WFLatticeCentralAnchorComponent, TransformComponent>();
            while (existing.MoveNext(out _, out var anchorXform))
            {
                if (anchorXform.GridUid == grid)
                    return;
            }

            if (FreeTileNearCentre(grid, gridComp) is not { } tile)
            {
                Log.Warning($"No free tile for the central anchor on {ToPrettyString(grid)}.");
                return;
            }

            Spawn(CentralPrototype, _maps.GridTileToLocal(grid, gridComp, tile));
            return;
        }
    }

    /// <summary>The free floor tile nearest the middle of a grid.</summary>
    private Vector2i? FreeTileNearCentre(EntityUid grid, MapGridComponent gridComp)
    {
        var centre = gridComp.LocalAABB.Center;
        Vector2i? best = null;
        var bestDistance = float.MaxValue;
        foreach (var tile in _maps.GetAllTiles(grid, gridComp))
        {
            var distance = (new Vector2(tile.GridIndices.X + 0.5f, tile.GridIndices.Y + 0.5f) - centre).LengthSquared();
            if (distance >= bestDistance || _turf.IsSpace(tile.Tile))
                continue;

            var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, tile.GridIndices);
            if (anchored.MoveNext(out _))
                continue;

            best = tile.GridIndices;
            bestDistance = distance;
        }

        return best;
    }

    private void OnCentralDestroyed(Entity<WFLatticeCentralAnchorComponent> ent, ref DestructionEventArgs args)
    {
        Announce("wf-lattice-central-lost", "wf-lattice-central-lost-title", "wf-lattice-central-lost-chip", BreachAlarm);
    }
}
