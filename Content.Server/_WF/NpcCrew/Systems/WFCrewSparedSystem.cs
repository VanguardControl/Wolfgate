using Content.Server._WF.NpcCrew.Components;
using Content.Shared._Mono.Company;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Player vessels of an NPC crew's own faction, IFF or none. The crew never warns, hunts or zones them, and answers
/// their fire only once it runs past <see cref="WFCrewAlertSystem.OwnFactionStrayHits"/>.
/// </summary>
public sealed class WFCrewSparedSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, HashSet<string>> _aboard = new();
    private GameTick _aboardTick;

    /// <summary>Whether <paramref name="target"/> is a player vessel of the faction <paramref name="grid"/> flies for.</summary>
    public bool Spared(EntityUid grid, EntityUid target)
    {
        // NPC crews settle their own fights; this only shields vessels players fly.
        if (grid == target || HasComp<WFCrewShipProfileComponent>(target))
            return false;

        var own = Company(grid);
        if (own.Length == 0)
            return false;

        // The people aboard decide, whatever flag the hull flies; an empty hull goes by its flag.
        var aboard = Aboard(target);
        if (aboard.Count == 0)
            return WFCompanyFamily.Same(own, Company(target));

        foreach (var company in aboard)
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
