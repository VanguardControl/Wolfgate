using Content.Server._WG.NpcCrew.Components;
using Content.Server.NPC.HTN;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>Keeps crewmen off each other's chores: a target another crewman's plan holds is taken.</summary>
public sealed class WGCrewClaimSystem : EntitySystem
{
    /// <summary>Whether a crewman other than <paramref name="owner"/> holds <paramref name="target"/> under a blackboard key.</summary>
    public bool ClaimedByOther(EntityUid owner, EntityUid target, string key)
    {
        var query = EntityQueryEnumerator<WGCrewComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out _, out var htn))
        {
            if (uid != owner && htn.Blackboard.TryGetValue<EntityUid>(key, out var held, EntityManager) && held == target)
                return true;
        }
        return false;
    }
}
