using Content.Server.Power.EntitySystems;
using Content.Shared._WF.AsteroidMining;
using Content.Shared.Examine;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Whitelist;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.AsteroidMining;

/// <summary>
/// Runs ore magnets: finds loose ore in range and in sight, locks onto it, and pulls it into storage once the lock has
/// held. A lock breaks if the ore drifts out of range or behind a wall, or the magnet loses power or is switched off.
/// </summary>
public sealed class WFOreMagnetSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    private readonly List<EntityUid> _done = new();
    private readonly HashSet<EntityUid> _nearby = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFOreMagnetComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<WFOreMagnetComponent> ent, ref ComponentShutdown args)
    {
        ReleaseAll(ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFOreMagnetComponent, StorageComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var magnet, out var storage, out var xform))
        {
            var active = xform.Anchored && _toggle.IsActivated(uid) && this.IsPowered(uid, EntityManager);
            if (active != magnet.Active)
            {
                magnet.Active = active;
                _appearance.SetData(uid, WFOreMagnetVisuals.Active, active);
                if (!active)
                    ReleaseAll(magnet);
            }

            if (!active)
                continue;

            UpdatePulls((uid, magnet, storage, xform), now);
            if (now < magnet.NextScan)
                continue;

            magnet.NextScan = now + magnet.ScanInterval;
            Scan((uid, magnet, storage, xform), now);
        }
    }

    /// <summary>Pulls in the ore whose lock has held, and drops any it has lost.</summary>
    private void UpdatePulls(Entity<WFOreMagnetComponent, StorageComponent, TransformComponent> ent, TimeSpan now)
    {
        var (uid, magnet, storage, xform) = ent;
        _done.Clear();
        foreach (var ore in magnet.Pulling)
        {
            if (TerminatingOrDeleted(ore) || !TryComp<WFOreMagnetPulledComponent>(ore, out var pulled) || !CanSee(uid, magnet, ore))
            {
                _done.Add(ore);
                continue;
            }

            if (now < pulled.End)
                continue;

            _done.Add(ore);
            var oreXform = Transform(ore);
            var from = _transform.ToCoordinates(_transform.GetMoverCoordinates(uid, xform).EntityId,
                _transform.GetMapCoordinates(ore, oreXform));
            var rotation = oreXform.LocalRotation;
            if (_storage.Insert(uid, ore, out var stacked, storageComp: storage))
                _storage.PlayPickupAnimation(stacked ?? ore, from, xform.Coordinates, rotation);
        }

        foreach (var ore in _done)
        {
            magnet.Pulling.Remove(ore);
            if (!TerminatingOrDeleted(ore))
                RemComp<WFOreMagnetPulledComponent>(ore);
        }
    }

    /// <summary>Locks onto loose ore in range and in sight, as far as free locks and storage space allow.</summary>
    private void Scan(Entity<WFOreMagnetComponent, StorageComponent, TransformComponent> ent, TimeSpan now)
    {
        var (uid, magnet, storage, _) = ent;
        if (magnet.Pulling.Count >= magnet.MaxPulls)
            return;

        // Room left, less what the ore already locked on will take.
        var free = storage.Grid.GetArea() - _storage.GetCumulativeItemAreas((uid, storage));
        foreach (var locked in magnet.Pulling)
        {
            if (TryComp<ItemComponent>(locked, out var lockedItem))
                free -= _item.GetItemShape((locked, lockedItem)).GetArea();
        }

        if (free <= 0)
            return;

        var origin = _transform.GetMapCoordinates(uid);
        _nearby.Clear();
        _lookup.GetEntitiesInRange(uid, magnet.Range, _nearby, LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var ore in _nearby)
        {
            if (magnet.Pulling.Count >= magnet.MaxPulls)
                break;

            if (HasComp<WFOreMagnetPulledComponent>(ore) ||
                !TryComp<PhysicsComponent>(ore, out var physics) || physics.BodyStatus != BodyStatus.OnGround ||
                _whitelist.IsWhitelistFail(storage.Whitelist, ore) ||
                !TryComp<ItemComponent>(ore, out var item) ||
                !CanSee(uid, magnet, ore))
                continue;

            var size = _item.GetItemShape((ore, item)).GetArea();
            if (size > free)
                continue;

            free -= size;
            var distance = (_transform.GetMapCoordinates(ore).Position - origin.Position).Length();
            var pulled = EnsureComp<WFOreMagnetPulledComponent>(ore);
            pulled.Magnet = uid;
            pulled.Start = now;
            pulled.End = now + TimeSpan.FromSeconds(magnet.PullTimeBase + distance * magnet.PullTimePerMetre);
            Dirty(ore, pulled);
            magnet.Pulling.Add(ore);
        }
    }

    /// <summary>Whether the ore is in range and nothing that blocks sight, such as a wall, stands between them.</summary>
    private bool CanSee(EntityUid magnet, WFOreMagnetComponent comp, EntityUid ore)
    {
        return _examine.InRangeUnOccluded(_transform.GetMapCoordinates(magnet), _transform.GetMapCoordinates(ore), comp.Range,
            e => e == magnet || e == ore);
    }

    private void ReleaseAll(WFOreMagnetComponent magnet)
    {
        foreach (var ore in magnet.Pulling)
        {
            if (!TerminatingOrDeleted(ore))
                RemComp<WFOreMagnetPulledComponent>(ore);
        }

        magnet.Pulling.Clear();
    }
}
