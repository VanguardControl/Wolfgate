using Content.Shared._WF.RoundSetup;
using NUnit.Framework;

namespace Content.Tests._WF.RoundSetup;

/// <summary>Faction recruitment follows actual jobs, not titles, foreign jobs or unknown ranks.</summary>
[TestFixture]
public sealed class WFFactionCrewPolicyTest
{
    [TestCase(WFCrewFaction.TSF, "Bailiff", "SeniorOfficer", true)]
    [TestCase(WFCrewFaction.TSF, "Bailiff", "Bailiff", true)]
    [TestCase(WFCrewFaction.TSF, "Bailiff", "Sheriff", false)]
    [TestCase(WFCrewFaction.TSF, "Deputy", "Brigmedic", true)]
    [TestCase(WFCrewFaction.TSF, "Deputy", "Pirate", false)]
    [TestCase(WFCrewFaction.PDV, "PirateFirstMate", "PDVDenasvar", true)]
    [TestCase(WFCrewFaction.PDV, "PirateFirstMate", "PirateCaptain", false)]
    [TestCase(WFCrewFaction.PDV, "PirateCaptain", "PDVPrince", false)]
    [TestCase(WFCrewFaction.None, "Sheriff", "Deputy", false)]
    [TestCase(WFCrewFaction.TSF, null, "Deputy", false)]
    [TestCase(WFCrewFaction.TSF, "Sheriff", null, false)]
    public void RecruitmentRequiresKnownRanks(WFCrewFaction faction, string host, string recruit, bool allowed)
    {
        Assert.That(WFFactionCrewPolicy.CanRecruit(faction, host, recruit), Is.EqualTo(allowed));
    }
}
