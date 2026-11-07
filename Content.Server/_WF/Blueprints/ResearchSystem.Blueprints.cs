using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    /// <summary>Unlocks every tier-1 technology of the server's disciplines, free.</summary>
    public void WFGrantBasicTechnologies(EntityUid server, TechnologyDatabaseComponent? db = null)
    {
        if (!Resolve(server, ref db, false))
            return;

        var supported = db.SupportedDisciplines;
        var unlocked = db.UnlockedTechnologies;
        var added = false;
        foreach (var tech in PrototypeManager.EnumeratePrototypes<TechnologyPrototype>())
        {
            if (tech.Tier != 1 || tech.Hidden || unlocked.Contains(tech.ID) || !supported.Contains(tech.Discipline))
                continue;

            AddTechnology(server, tech, db);
            added = true;
        }

        if (added)
            UpdateTechnologyCards(server, db);
    }
}
