using System.Numerics;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Examine;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>
/// Server half of the cracker hull: it holds the crack state, resolves the mapper-placed chunk berth and owns every
/// state transition. The geometry lives in SharedWFCrackerSystem so the client diagrams derive it the same way.
/// </summary>
public sealed partial class WFCrackerSystem : SharedWFCrackerSystem
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFPlanetCrackerComponent, MapInitEvent>(OnCrackerMapInit);
        SubscribeLocalEvent<WFChunkBerthComponent, MapInitEvent>(OnBerthMapInit);
        SubscribeLocalEvent<WFChunkBerthComponent, ExaminedEvent>(OnBerthExamined);

        // The state machine's six broadcast anchor subscriptions; a partial class may only carry one Initialize.
        InitializeStateMachine();

        // The disconnect protocol's three broadcast subscriptions, for the same reason.
        InitializeDisconnect();
    }

    /// <summary>A freshly loaded hull starts idle and looks for the berth marker its mapper placed on it.</summary>
    private void OnCrackerMapInit(Entity<WFPlanetCrackerComponent> ent, ref MapInitEvent args)
    {
        SetState(ent, WFCrackState.Idle);

        if (ent.Comp.Berth is null)
        {
            var query = EntityQueryEnumerator<WFChunkBerthComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != ent.Owner)
                    continue;

                ent.Comp.Berth = GetNetEntity(uid);
                break;
            }
        }

        UpdateBerthPose(ent);
        Dirty(ent);
    }

    /// <summary>Back-link for the other map-init order: a berth that initialises after its hull still registers itself.</summary>
    private void OnBerthMapInit(Entity<WFChunkBerthComponent> ent, ref MapInitEvent args)
    {
        if (Transform(ent.Owner).GridUid is not { } grid)
            return;

        if (!TryComp<WFPlanetCrackerComponent>(grid, out var cracker) || cracker.Berth is not null)
            return;

        cracker.Berth = GetNetEntity(ent.Owner);
        UpdateBerthPose((grid, cracker));
        Dirty(grid, cracker);
    }

    /// <summary>Tells a mapper how big the berth is and how far out it sits.</summary>
    private void OnBerthExamined(Entity<WFChunkBerthComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-berth-examine",
            ("width", ent.Comp.Size.X),
            ("height", ent.Comp.Size.Y),
            ("distance", MathF.Round(ent.Comp.Distance, 1))));
    }

    /// <summary>
    /// Copies the resolved berth's grid-local pose onto the grid component; public so a later sweep can refresh it.
    /// The grid entity is force-sent to anyone who sees any chunk of it while the marker itself is routinely outside
    /// net.pvs_range, so this is the only route the berth pose has to a client with no BUI state to read.
    /// </summary>
    public void UpdateBerthPose(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.Berth is not { } netBerth || !TryGetEntity(netBerth, out var berth))
            return;

        if (!TryComp<WFChunkBerthComponent>(berth, out var berthComp))
            return;

        var (worldPos, worldRot) = TransformSystem.GetWorldPositionRotation(berth.Value);
        var invMatrix = TransformSystem.GetInvWorldMatrix(ent.Owner);

        // The field is the berth CENTRE, which is Distance tiles out along the marker's own facing - the same
        // derivation TryGetBerthCentre makes. Storing the marker pose instead would draw the radar ghost's rectangle
        // on the marker, and Distance is not networked, so no consumer could recover the offset.
        var pos = Vector2.Transform(worldPos + worldRot.ToWorldVec() * berthComp.Distance, invMatrix);
        var rot = worldRot - TransformSystem.GetWorldRotation(ent.Owner);

        // The sweep re-runs this, so nothing moved means nothing on the wire: the same inputs recompute bit-identically.
        if (pos == ent.Comp.BerthLocalPos && rot == ent.Comp.BerthLocalRot && berthComp.Size == ent.Comp.BerthSize)
            return;

        ent.Comp.BerthLocalPos = pos;
        ent.Comp.BerthLocalRot = rot;
        ent.Comp.BerthSize = berthComp.Size;
        Dirty(ent);
    }

    /// <summary>Moves a cracker to a new crack stage; the only writer of the state field.</summary>
    public void SetState(Entity<WFPlanetCrackerComponent> ent, WFCrackState state)
    {
        if (ent.Comp.State == state)
            return;

        var old = ent.Comp.State;
        ent.Comp.State = state;
        Dirty(ent);

        // Raised after the Dirty, and never on a no-op because of the early return above.
        var ev = new WFCrackStateChangedEvent(ent.Owner, old, state);
        RaiseLocalEvent(ref ev);
    }
}
