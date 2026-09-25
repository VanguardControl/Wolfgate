using System.Numerics;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Examine;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>Server half of the cracker hull: the crack state, the chunk berth and every state transition.</summary>
public sealed partial class WFCrackerSystem : SharedWFCrackerSystem
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFPlanetCrackerComponent, MapInitEvent>(OnCrackerMapInit);
        SubscribeLocalEvent<WFChunkBerthComponent, MapInitEvent>(OnBerthMapInit);
        SubscribeLocalEvent<WFChunkBerthComponent, ExaminedEvent>(OnBerthExamined);

        InitializeStateMachine();

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

    /// <summary>Copies the berth's grid-local pose onto the hull; the marker is often outside client PVS.</summary>
    public void UpdateBerthPose(Entity<WFPlanetCrackerComponent> ent)
    {
        if (ent.Comp.Berth is not { } netBerth || !TryGetEntity(netBerth, out var berth))
            return;

        if (!TryComp<WFChunkBerthComponent>(berth, out var berthComp))
            return;

        var (worldPos, worldRot) = TransformSystem.GetWorldPositionRotation(berth.Value);
        var invMatrix = TransformSystem.GetInvWorldMatrix(ent.Owner);

        // The berth centre, Distance tiles along the marker's facing; Distance isn't networked, so store the centre.
        var pos = Vector2.Transform(worldPos + worldRot.ToWorldVec() * berthComp.Distance, invMatrix);
        var rot = worldRot - TransformSystem.GetWorldRotation(ent.Owner);

        // The sweep re-runs this; unchanged inputs recompute bit-identically and send nothing.
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

        var ev = new WFCrackStateChangedEvent(ent.Owner, old, state);
        RaiseLocalEvent(ref ev);
    }
}
