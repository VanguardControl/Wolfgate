using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>True when this map is a planet orbit layer, where parked grids never fall.</summary>
    private bool WfIsOrbitLayer(EntityUid mapUid) => HasComp<WFOrbitLayerComponent>(mapUid);

    /// <summary>
    /// Holds a grid up on a planet orbit layer, and hands its z-gravity back when it leaves.
    /// The fall gate's own orbit exemption only covers the plummet path. Orbit is bare vacuum with no terrain anywhere
    /// on it, so a grid parked there reads a ground height of -1 (the shared height walk finds no tile on the orbit map
    /// and none on the layer below either), the shared z-physics integrator sees its local height cross zero on the very
    /// first tick and calls TryMoveDown, and that walks the grid down one map per level with a direct parent change -
    /// past every air layer and onto the terrain, with no transit map and no crash. The only thing that ever kept a hull
    /// up there was standing on one of its own tiles, which is pure luck about where its grid origin happens to fall.
    /// Orbit is above the atmosphere: nothing is pulling on a hull parked there, so it keeps its height without
    /// integrating gravity at all.
    /// </summary>
    private void WfRefreshOrbitParking(EntityUid grid, EntityUid? mapUid)
    {
        if (!ZPhysicsQuery.TryComp(grid, out var zPhys))
            return;

        var parked = mapUid is { } map && WfIsOrbitLayer(map);

        zPhys.VelocityGravity = !parked;

        if (!parked)
            return;

        // Whatever speed and height the arrival left behind is spent: a hull in orbit is at rest on its own level.
        if (zPhys.Velocity != 0f)
            SetZVelocity((grid, zPhys), 0f);

        if (zPhys.LocalPosition != 0f)
            SetZPosition((grid, zPhys), 0f);
    }

    /// <summary>
    /// True when a grid must not be handed straight to the layer below by the shared level-hop path. Orbit is the one
    /// layer you leave deliberately: a descent from it goes through the transit machinery like every other flight, under
    /// power if the hull has lift and as a real fall if it does not. The backstop matters even with the parking above,
    /// because a level hop is the only way a grid ever changes z-map without a transit map in between.
    /// </summary>
    private bool WfRefusesLevelHop(EntityUid grid) => WfIsOrbitLayer(Transform(grid).MapUid ?? EntityUid.Invalid);


    /// <summary>Test seam: whether this grid is currently parked by the orbit hold.</summary>
    public bool WfIsParkedInOrbit(Entity<MapGridComponent> grid)
    {
        return WfIsOrbitLayer(Transform(grid).MapUid ?? EntityUid.Invalid)
               && ZPhysicsQuery.TryComp(grid, out var zPhys)
               && !zPhys.VelocityGravity;
    }

    /// <summary>
    /// Destroys every contact held by the grid's own children before the grid changes map. A grid move only
    /// re-homes the grid's broadphase; a child anchored on it that was touching something on the old map (a gravity
    /// anchor against a fissure mob, say) keeps that contact, and the broadphase asserts "already in contact" the
    /// moment the grid comes back within reach of it.
    /// </summary>
    public void WfDestroyRiderContacts(EntityUid grid)
    {
        var children = Transform(grid).ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            if (TryComp<PhysicsComponent>(child, out var body))
                _physics.DestroyContacts(body);
        }
    }
}
