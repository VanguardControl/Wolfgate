// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Shared._WF.Xenonids.Evolution;

public sealed partial class XenoEvolutionSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;

    public bool HasLiving<T>(int count, Predicate<Entity<T>>? predicate = null) where T : IComponent
    {
        if (count <= 0)
            return true;

        var total = 0;
        var query = EntityQueryEnumerator<T>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (TryComp(uid, out MobStateComponent? mobState) && _mobState.IsDead(uid, mobState))
                continue;

            if (predicate != null && !predicate((uid, comp)))
                continue;

            total++;
            if (total >= count)
                return true;
        }

        return false;
    }
}
