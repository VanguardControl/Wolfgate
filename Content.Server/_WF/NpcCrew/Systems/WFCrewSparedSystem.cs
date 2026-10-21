using Content.Server._WF.NpcCrew.Components;
using Content.Shared._Mono.Company;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Player vessels an NPC crew never turns on, whatever they do: its own faction's. Every ship hostility decision checks
/// this before anything else.
/// </summary>
public sealed class WFCrewSparedSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, HashSet<string>> _aboard = new();
    private GameTick _aboardTick;

    /// <summary>Whether the crew of <paramref name="grid"/> must never alert on, hunt or fire on <paramref name="target"/>.</summary>
    public bool Spared(EntityUid grid, EntityUid target)
    {
        // NPC crews settle their own fights; this only shields vessels players fly.
        if (grid == target || HasComp<WFCrewShipProfileComponent>(target))
            return false;

        var own = Company(grid);
        if (own.Length == 0)
            return false;

        var theirs = Company(target);
        if (theirs.Length > 0)
            return WFCompanyFamily.Same(own, theirs);

        // A vessel with no flag of its own flies the colours of the people aboard.
        foreach (var company in Aboard(target))
        {
            if (WFCompanyFamily.Same(own, company))
                return true;
        }

        return false;
    }

    private string Company(EntityUid uid)
    {
        return CompOrNull<CompanyComponent>(uid)?.CompanyName.Id is { } id && id != "None" ? id : string.Empty;
    }

    /// <summary>The companies of the players on a grid, gathered once a tick.</summary>
    private IReadOnlyCollection<string> Aboard(EntityUid grid)
    {
        if (_aboardTick != _timing.CurTick)
        {
            _aboardTick = _timing.CurTick;
            _aboard.Clear();
            var players = EntityQueryEnumerator<ActorComponent, CompanyComponent, TransformComponent>();
            while (players.MoveNext(out _, out _, out var company, out var xform))
            {
                if (xform.GridUid is not { } on || company.CompanyName.Id is not { Length: > 0 } id || id == "None")
                    continue;
                if (!_aboard.TryGetValue(on, out var set))
                    _aboard[on] = set = new HashSet<string>();
                set.Add(id);
            }
        }

        return _aboard.TryGetValue(grid, out var found) ? found : Array.Empty<string>();
    }
}
