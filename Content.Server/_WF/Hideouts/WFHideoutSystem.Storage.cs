using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Storage.Components;
using Content.Shared._WF.Hideouts;
using Content.Shared.Examine;
using Content.Shared.Item;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Hideouts;

/// <summary>
/// What a hideout keeps between rounds: its floor and everything built on it, but of its goods only what its safes
/// hold. Safes take storage points, of which an outpost has few, and take only what their whitelist lists.
/// </summary>
public sealed partial class WFHideoutSystem
{
    private readonly List<(EntityUid Uid, Vector2i Tile, int Points)> _stashes = new();

    private void InitializeStorage()
    {
        SubscribeLocalEvent<WFOutpostCargoEvent>(OnCargo);
        SubscribeLocalEvent<WFOutpostPrefabLaidEvent>(OnPrefabLaid);
        SubscribeLocalEvent<WFHideoutStashComponent, ExaminedEvent>(OnStashExamined);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => ClearPalette());
    }

    /// <summary>The safes anchored on a grid, in an order that is the same before a save and after a load.</summary>
    private List<(EntityUid Uid, Vector2i Tile, int Points)> Stashes(EntityUid grid)
    {
        _stashes.Clear();
        var query = EntityQueryEnumerator<WFHideoutStashComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var stash, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                _stashes.Add((uid, new Vector2i((int) MathF.Floor(xform.LocalPosition.X), (int) MathF.Floor(xform.LocalPosition.Y)), stash.Points));
        }

        _stashes.Sort((a, b) => a.Tile.Y != b.Tile.Y ? a.Tile.Y.CompareTo(b.Tile.Y) : a.Tile.X.CompareTo(b.Tile.X));
        return _stashes;
    }

    /// <summary>Storage points the safes on an outpost and its basement take.</summary>
    public int StashUsed(EntityUid outpost)
    {
        var used = 0;
        foreach (var stash in Stashes(outpost))
            used += stash.Points;

        if (TryComp<WFOutpostComponent>(outpost, out var comp) && comp.Basement is { } basement && !TerminatingOrDeleted(basement))
        {
            foreach (var stash in Stashes(basement))
                used += stash.Points;
        }

        return used;
    }

    /// <summary>The safes on a grid whose contents a save keeps: those that fit the storage points, the outpost's own first.</summary>
    private HashSet<EntityUid> KeptStashes(EntityUid grid)
    {
        var left = HideoutRules.StashPoints;
        if (TryComp<WFOutpostBasementComponent>(grid, out var basement) && !TerminatingOrDeleted(basement.Outpost))
        {
            foreach (var stash in Stashes(basement.Outpost))
            {
                if (stash.Points <= left)
                    left -= stash.Points;
            }
        }

        var kept = new HashSet<EntityUid>();
        foreach (var stash in Stashes(grid))
        {
            if (stash.Points > left)
                continue;

            left -= stash.Points;
            kept.Add(stash.Uid);
        }

        return kept;
    }

    /// <summary>
    /// What a save of a hideout leaves behind: loose items and crates, and what lockers and other storage hold. Safes
    /// within the storage points keep what they hold; stakes, packed machines and decoration stay where they lie.
    /// </summary>
    private void OnCargo(ref WFOutpostCargoEvent args)
    {
        if (!_cfg.GetCVar(HideoutCVars.Enabled))
            return;

        var kept = KeptStashes(args.Grid);
        var children = Transform(args.Grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (kept.Contains(child))
                continue;

            var loose = !Transform(child).Anchored && (HasComp<ItemComponent>(child) || HasComp<EntityStorageComponent>(child));
            if (loose && !HasComp<WFHideoutDecorComponent>(child))
            {
                // A stake or a packed machine was paid for at the console, and is the outpost's as much as what it lays.
                if (!HasComp<WFHideoutStakeComponent>(child) && !HasComp<WFDeployableComponent>(child))
                    AddWithContents(child, args.Cargo);
                continue;
            }

            if (TryComp<EntityStorageComponent>(child, out var closet))
            {
                foreach (var held in closet.Contents.ContainedEntities)
                    AddWithContents(held, args.Cargo);
            }

            if (TryComp<StorageComponent>(child, out var storage))
            {
                foreach (var held in storage.Container.ContainedEntities)
                    AddWithContents(held, args.Cargo);
            }
        }
    }

    private void AddWithContents(EntityUid uid, HashSet<EntityUid> set)
    {
        var list = new List<EntityUid>();
        AddWithContents(uid, list);
        set.UnionWith(list);
    }

    /// <summary>Whatever a prefab sets out loose is decoration: it stays with the outpost.</summary>
    private void OnPrefabLaid(ref WFOutpostPrefabLaidEvent args)
    {
        MarkDecor(args.Grid);
    }

    /// <summary>Marks the loose items lying on a grid as decoration.</summary>
    private void MarkDecor(EntityUid grid)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!Transform(child).Anchored && HasComp<ItemComponent>(child))
                EnsureComp<WFHideoutDecorComponent>(child);
        }
    }

    private void OnStashExamined(Entity<WFHideoutStashComponent> stash, ref ExaminedEvent args)
    {
        var xform = Transform(stash);
        if (!xform.Anchored || xform.GridUid is not { } grid || !HasComp<MapGridComponent>(grid)
            || !HasComp<WFOutpostComponent>(grid) && !HasComp<WFOutpostBasementComponent>(grid))
        {
            args.PushMarkup(Loc.GetString("wf-hideout-stash-examine-loose", ("points", stash.Comp.Points)));
            return;
        }

        args.PushMarkup(Loc.GetString(KeptStashes(grid).Contains(stash) ? "wf-hideout-stash-examine-kept" : "wf-hideout-stash-examine-over",
            ("points", stash.Comp.Points), ("budget", HideoutRules.StashPoints)));
    }
}
