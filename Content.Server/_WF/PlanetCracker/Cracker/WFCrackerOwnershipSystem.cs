using Content.Shared._Mono.Shipyard;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>
/// Stamps the anchors and crates riding on a cracker hull with that hull, so two crackers can never share a pair.
/// </summary>
public sealed partial class WFCrackerOwnershipSystem : EntitySystem
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // ShipyardShuttlePurchaseEvent carries no [ByRefEvent] and both raise sites are broadcast, so match them exactly.
        SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>(OnPurchased);
        // Anything unowned that comes to rest on a cracker's deck becomes that cracker's: a crate bought from cargo
        // and carried aboard, or an anchor brought up from another site. Owned ones keep their owner.
        SubscribeLocalEvent<WFAnchorCrateComponent, EntParentChangedMessage>(OnCrateParentChanged);
        SubscribeLocalEvent<WFGravityAnchorComponent, EntParentChangedMessage>(OnAnchorParentChanged);
    }

    /// <summary>Binds an unowned crate to the cracker whose deck it just landed on.</summary>
    private void OnCrateParentChanged(Entity<WFAnchorCrateComponent> ent, ref EntParentChangedMessage args)
    {
        if (ent.Comp.Cracker is not null || !TryGetCrackerUnder(ent.Owner, out var cracker))
            return;

        ent.Comp.Cracker = GetNetEntity(cracker);
        Dirty(ent);
    }

    /// <summary>Binds an unowned anchor to the cracker whose deck it just landed on.</summary>
    private void OnAnchorParentChanged(Entity<WFGravityAnchorComponent> ent, ref EntParentChangedMessage args)
    {
        if (ent.Comp.Cracker is not null || !TryGetCrackerUnder(ent.Owner, out var cracker))
            return;

        ent.Comp.Cracker = GetNetEntity(cracker);
        Dirty(ent);
    }

    /// <summary>The cracker hull this entity now rests on, if its grid is one.</summary>
    private bool TryGetCrackerUnder(EntityUid uid, out EntityUid cracker)
    {
        cracker = EntityUid.Invalid;

        if (Transform(uid).GridUid is not { } grid || !HasComp<WFPlanetCrackerComponent>(grid))
            return false;

        cracker = grid;
        return true;
    }

    /// <summary>A freshly bought cracker owns whatever anchors shipped aboard it, and gets its transport docked on.</summary>
    private void OnPurchased(ShipyardShuttlePurchaseEvent args)
    {
        if (!TryComp<WFPlanetCrackerComponent>(args.Shuttle, out var cracker))
            return;

        BindAboard(args.Shuttle);
        TrySpawnTransport((args.Shuttle, cracker));
    }

    /// <summary>Stamps every unowned anchor and crate resting on this cracker as belonging to it; returns how many were bound.</summary>
    public int BindAboard(EntityUid cracker)
    {
        return BindAboard(cracker, cracker);
    }

    /// <summary>
    /// Stamps the unowned anchors and crates riding on <paramref name="grid"/> as the cracker's; returns how many were
    /// bound. The grid is the cracker itself on a purchase, or a transport docked to it.
    /// </summary>
    public int BindAboard(EntityUid cracker, EntityUid grid)
    {
        var owner = GetNetEntity(cracker);
        var bound = 0;

        var crates = EntityQueryEnumerator<WFAnchorCrateComponent, TransformComponent>();
        while (crates.MoveNext(out var uid, out var crate, out var xform))
        {
            if (xform.GridUid != grid || crate.Cracker is not null)
                continue;

            crate.Cracker = owner;
            Dirty(uid, crate);
            bound++;
        }

        var anchors = EntityQueryEnumerator<WFGravityAnchorComponent, TransformComponent>();
        while (anchors.MoveNext(out var uid, out var anchor, out var xform))
        {
            if (xform.GridUid != grid || anchor.Cracker is not null)
                continue;

            anchor.Cracker = owner;
            Dirty(uid, anchor);
            bound++;
        }

        return bound;
    }
}
