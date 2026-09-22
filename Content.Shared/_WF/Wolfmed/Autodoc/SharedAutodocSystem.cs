using Content.Shared.Body.Components;
using Content.Shared.DragDrop;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>
/// The half of the autodoc both sides need: whether a body is lying inside a pod. The surgery system asks
/// this instead of looking for a buckle and an operating table, so a pod occupant satisfies every surgery
/// that wants a table.
/// </summary>
public sealed class SharedAutodocSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();
        // The drag-and-drop check runs on the client too; answered only on the server it came back "unknown"
        // and whether a drop worked depended on whatever else sat on the pod.
        SubscribeLocalEvent<AutodocComponent, CanDropTargetEvent>(OnCanDropTarget);
    }

    private void OnCanDropTarget(Entity<AutodocComponent> ent, ref CanDropTargetEvent args)
    {
        if (args.Handled)
            return;

        var empty = !_container.TryGetContainer(ent.Owner, AutodocComponent.BodyContainerId, out var container) ||
                    container.ContainedEntities.Count == 0;
        args.CanDrop = empty && !ent.Comp.Locked && HasComp<BodyComponent>(args.Dragged);
        args.Handled = true;
    }

    /// <summary>The pod this body is lying in, or null.</summary>
    public Entity<AutodocComponent>? GetPod(EntityUid body)
    {
        if (!_container.TryGetContainingContainer((body, null, null), out var container) ||
            container.ID != AutodocComponent.BodyContainerId ||
            !TryComp(container.Owner, out AutodocComponent? autodoc))
            return null;

        return (container.Owner, autodoc);
    }

    /// <summary>True while the body is inside a pod, which is what the operating-table condition wants.</summary>
    public bool OnOperatingPlatform(EntityUid body) => GetPod(body) != null;
}
