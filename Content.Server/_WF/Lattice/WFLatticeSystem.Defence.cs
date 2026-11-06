using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Shared._WF.Announcements;
using Content.Shared._WF.Lattice;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Lattice;

/// <summary>
/// An anchor station's answer to being shot at. A few hits on its shield are forgiven; past the profile's limit, or
/// with any hit on the core itself, the attacking grid is marked hostile: the station sounds the alert, calls its drones
/// and turns its lances on that grid until it has been quiet a while. The lances fire only from an anchor station.
/// </summary>
public sealed partial class WFLatticeSystem
{
    [Dependency] private FireControlSystem _fireControl = default!;

    /// <summary>Seconds between the lances' aiming passes.</summary>
    private const float DefenceInterval = 0.25f;

    private float _defenceAccumulator;
    private readonly List<EntityUid> _expired = new();

    private void InitializeDefence()
    {
        SubscribeLocalEvent<WFLatticeDefenceWeaponComponent, ShotAttemptedEvent>(OnLanceShotAttempted);
    }

    /// <summary>A lance taken off its anchor station is dead weight.</summary>
    private void OnLanceShotAttempted(Entity<WFLatticeDefenceWeaponComponent> ent, ref ShotAttemptedEvent args)
    {
        if (Transform(ent.Owner).GridUid is not { } grid || !HasComp<WFLatticeAnchorStationComponent>(grid))
            args.Cancel();
    }

    /// <summary>Counts a hit on a station from a grid; enough of them, or a heavy one, make that grid hostile.</summary>
    public void Strike(EntityUid station, EntityUid? attacker, float weight)
    {
        if (attacker is not { } enemy
            || enemy == station
            || !Exists(enemy)
            || !TryComp<WFLatticeAnchorStationComponent>(station, out var comp)
            || !TryGetProfile(station, out var profile))
            return;

        var now = _timing.CurTime;
        var strikes = comp.Strikes.TryGetValue(enemy, out var last) && (now - last.Last).TotalSeconds < profile.StrikeWindow
            ? last.Strikes
            : 0f;
        strikes += weight;
        comp.Strikes[enemy] = (strikes, now);

        if (comp.Hostiles.ContainsKey(enemy) || strikes >= profile.StrikeLimit)
        {
            MarkHostile((station, comp), enemy, profile);
            return;
        }

        // The first stray shot gets a warning to the ship that fired it, not a war.
        if (strikes <= weight)
            Warn(station, enemy);
    }

    /// <summary>Turns the station on a grid: refreshes how long it stays hostile, and alerts and calls drones if due.</summary>
    private void MarkHostile(Entity<WFLatticeAnchorStationComponent> station, EntityUid enemy, WFLatticeProfilePrototype profile)
    {
        var now = _timing.CurTime;
        station.Comp.Hostiles[enemy] = now + TimeSpan.FromSeconds(profile.HostileSeconds);
        Defend(station.Owner, enemy);
        if (now < station.Comp.NextAlert)
            return;

        station.Comp.NextAlert = now + TimeSpan.FromSeconds(profile.AlertCooldown);
        var anchor = Exists(station.Comp.Core) ? Name(station.Comp.Core) : Name(station.Owner);
        var text = Loc.GetString("wf-lattice-defence", ("anchor", anchor), ("attacker", Name(enemy)));
        _chat.DispatchGlobalAnnouncement(text, Loc.GetString("wf-lattice-chip-sender"), true, BreachSound, Color.FromHex("#d030a0"));
        RaiseNetworkEvent(new WFAnnouncementChipEvent
        {
            Level = WFAnnounceChipLevel.Warning,
            Accent = Color.FromHex("#d030a0"),
            Title = Loc.GetString("wf-lattice-defence-title"),
            Text = text,
            Sender = Loc.GetString("wf-lattice-chip-sender"),
            Duration = 20f,
            Flash = true,
        });
    }

    /// <summary>Tells the crew of a grid that fired on a station to stop.</summary>
    private void Warn(EntityUid station, EntityUid enemy)
    {
        var anchor = TryComp<WFLatticeAnchorStationComponent>(station, out var comp) && Exists(comp.Core)
            ? Name(comp.Core)
            : Name(station);
        var text = Loc.GetString("wf-lattice-defence-warning", ("anchor", anchor));
        var chip = new WFAnnouncementChipEvent
        {
            Level = WFAnnounceChipLevel.Advisory,
            Accent = Color.FromHex("#d030a0"),
            Title = Loc.GetString("wf-lattice-defence-title"),
            Text = text,
            Sender = Loc.GetString("wf-lattice-chip-sender"),
            Duration = 12f,
        };

        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is { } mob && Transform(mob).GridUid == enemy)
                RaiseNetworkEvent(chip, session);
        }
    }

    /// <summary>Every quarter second, each station with a hostile in reach lays its lances on the nearest and fires.</summary>
    private void UpdateDefence(float frameTime)
    {
        _defenceAccumulator += frameTime;
        if (_defenceAccumulator < DefenceInterval)
            return;

        _defenceAccumulator = 0f;
        var now = _timing.CurTime;
        var stations = EntityQueryEnumerator<WFLatticeAnchorStationComponent, TransformComponent>();
        while (stations.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Hostiles.Count == 0)
                continue;

            _expired.Clear();
            foreach (var (enemy, until) in comp.Hostiles)
            {
                if (now >= until || TerminatingOrDeleted(enemy))
                    _expired.Add(enemy);
            }

            foreach (var enemy in _expired)
            {
                comp.Hostiles.Remove(enemy);
                comp.Strikes.Remove(enemy);
            }

            if (comp.Hostiles.Count == 0 || !TryGetProfile(uid, out var profile))
                continue;

            var from = _transform.GetWorldPosition(xform);
            EntityUid? target = null;
            var best = profile.DefenceRange;
            foreach (var enemy in comp.Hostiles.Keys)
            {
                var enemyXform = Transform(enemy);
                if (enemyXform.MapUid != xform.MapUid)
                    continue;

                var distance = Vector2.Distance(from, _transform.GetWorldPosition(enemyXform));
                if (distance > best)
                    continue;

                best = distance;
                target = enemy;
            }

            if (target is { } aim)
                FireLances(uid, aim);
        }
    }

    /// <summary>Fires every lance on a station at a grid, leading it by its speed.</summary>
    private void FireLances(EntityUid station, EntityUid target)
    {
        var targetXform = Transform(target);
        var targetPos = _transform.GetWorldPosition(targetXform);
        var velocity = TryComp<PhysicsComponent>(target, out var body) ? body.LinearVelocity : Vector2.Zero;
        var lances = EntityQueryEnumerator<WFLatticeDefenceWeaponComponent, TransformComponent>();
        while (lances.MoveNext(out var lance, out _, out var xform))
        {
            if (xform.GridUid != station || !xform.Anchored)
                continue;

            var from = _transform.GetWorldPosition(xform);
            var speed = TryComp<GunComponent>(lance, out var gun) ? MathF.Max(gun.ProjectileSpeedModified, 1f) : 500f;
            var lead = targetPos + velocity * (Vector2.Distance(from, targetPos) / speed);
            _fireControl.AttemptFire(lance, lance, _transform.ToCoordinates(new Robust.Shared.Map.MapCoordinates(lead, xform.MapID)), noServer: true);
        }
    }

    private bool TryGetProfile(EntityUid station, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WFLatticeProfilePrototype? profile)
    {
        profile = null;
        return Transform(station).MapUid is { } map
               && TryComp<WFLatticeFieldComponent>(map, out var field)
               && _proto.TryIndex(field.Profile, out profile);
    }
}
