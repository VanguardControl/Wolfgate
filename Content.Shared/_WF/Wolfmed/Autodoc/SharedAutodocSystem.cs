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
