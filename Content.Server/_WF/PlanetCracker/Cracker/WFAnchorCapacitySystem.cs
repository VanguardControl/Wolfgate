using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>
/// Counts the anchors each hull is carrying: the rating examine and overload popup, and the virtual mass the lift check reads (design D11).
/// </summary>
public sealed partial class WFAnchorCapacitySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Colour of the examine line while the hull is within its rating.</summary>
    private const string WithinColour = "#7ee787";

    /// <summary>Colour of the examine line once the hull is over its rating.</summary>
    private const string OverColour = "#ff6b6b";

    /// <summary>Per-grid tally of carried anchors, rebuilt from scratch on every sweep.</summary>
    private readonly Dictionary<EntityUid, (int Count, float Mass)> _tally = new();

    /// <summary>Grids whose load component has nothing left to report; collected so the sweep does not remove mid-enumeration.</summary>
    private readonly List<EntityUid> _stale = new();

    /// <summary>Next tick of the 1 Hz sweep.</summary>
    private TimeSpan _nextSweep;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFAnchorCapacityComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>Examine: what the generator is rated for and what is aboard, green inside the rating and red over it.</summary>
    private void OnExamined(Entity<WFAnchorCapacityComponent> ent, ref ExaminedEvent args)
    {
        var text = Loc.GetString("wf-transport-capacity-examine",
            ("capacity", ent.Comp.Capacity),
            ("aboard", ent.Comp.Aboard));

        var colour = ent.Comp.Aboard > ent.Comp.Capacity ? OverColour : WithinColour;
        args.PushMarkup($"[color={colour}]{text}[/color]");
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + TimeSpan.FromSeconds(1);

        _tally.Clear();

        var crates = EntityQueryEnumerator<WFAnchorCrateComponent, TransformComponent>();
        while (crates.MoveNext(out _, out var crate, out var crateXform))
        {
            if (TryGetCarrier(crateXform, out var grid))
                Tally(grid, crate.VirtualMass);
        }

        var anchors = EntityQueryEnumerator<WFGravityAnchorComponent, TransformComponent>();
        while (anchors.MoveNext(out _, out var anchor, out var anchorXform))
        {
            if (TryGetCarrier(anchorXform, out var grid))
                Tally(grid, anchor.VirtualMass);
        }

        _stale.Clear();

        var loads = EntityQueryEnumerator<WFGridAnchorLoadComponent>();
        while (loads.MoveNext(out var uid, out _))
        {
            if (!_tally.ContainsKey(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            RemComp<WFGridAnchorLoadComponent>(uid);
        }

        foreach (var (grid, entry) in _tally)
        {
            var load = EnsureComp<WFGridAnchorLoadComponent>(grid);

            if (load.VirtualMass.Equals(entry.Mass))
                continue;

            load.VirtualMass = entry.Mass;
            Dirty(grid, load);
        }

        var rated = EntityQueryEnumerator<WFAnchorCapacityComponent, TransformComponent>();
        while (rated.MoveNext(out var uid, out var comp, out var xform))
        {
            var aboard = xform.GridUid is { } grid && _tally.TryGetValue(grid, out var entry) ? entry.Count : 0;

            if (aboard == comp.Aboard)
                continue;

            var wasOver = comp.Aboard > comp.Capacity;
            comp.Aboard = aboard;
            Dirty(uid, comp);

            // Only on the rising edge, or the popup would repeat every second for as long as the load sits there.
            if (aboard > comp.Capacity && !wasOver)
                _popup.PopupEntity(Loc.GetString("wf-transport-capacity-exceeded"), uid, PopupType.LargeCaution);
        }
    }

    /// <summary>The grid an anchor rides on as cargo: a wrenched-down rig is terrain, and a planet ground layer carries nothing.</summary>
    private bool TryGetCarrier(TransformComponent xform, out EntityUid grid)
    {
        grid = default;

        if (xform.Anchored || xform.GridUid is not { } gridUid)
            return false;

        // A planet ground layer is itself a grid; adding cargo mass to it could flip the whole network's pooled lift.
        if (HasComp<WFPlanetLayerComponent>(gridUid))
            return false;

        grid = gridUid;
        return true;
    }

    /// <summary>Adds one carried anchor and its virtual mass to a grid's running tally.</summary>
    private void Tally(EntityUid grid, float mass)
    {
        _tally.TryGetValue(grid, out var entry);
        _tally[grid] = (entry.Count + 1, entry.Mass + mass);
    }
}
