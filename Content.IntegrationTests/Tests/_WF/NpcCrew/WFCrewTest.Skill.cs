#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>Each skill level is no worse than the one below it, and a mission's skill reaches every crewman.</summary>
    [Test]
    public async Task CrewSkillScalesAndReachesTheCrew()
    {
        var levels = Enum.GetValues<WFCrewSkill>().Select(WFCrewSkills.Of).ToArray();
        for (var i = 1; i < levels.Length; i++)
        {
            Assert.That(levels[i].AimError, Is.LessThanOrEqualTo(levels[i - 1].AimError));
            Assert.That(levels[i].ShootDelay, Is.LessThanOrEqualTo(levels[i - 1].ShootDelay));
            Assert.That(levels[i].GunneryError, Is.LessThanOrEqualTo(levels[i - 1].GunneryError));
            Assert.That(levels[i].Leading, Is.GreaterThanOrEqualTo(levels[i - 1].Leading));
            Assert.That(levels[i].Handling, Is.GreaterThanOrEqualTo(levels[i - 1].Handling));
            Assert.That(levels[i].Evasion, Is.GreaterThanOrEqualTo(levels[i - 1].Evasion));
        }

        Assert.That(WFCrewSkills.Of(WFCrewSkill.Green).DodgesFire, Is.False);
        Assert.That(WFCrewSkills.Of(WFCrewSkill.Elite).AimError, Is.Zero);

        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var hand = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Deckhand,
                new Robust.Shared.Map.EntityCoordinates(deck, new Vector2(2.5f)), "skill")!.Value;
            SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(hand).Enabled = false;
            Assert.That(SEntMan.GetComponent<WFCrewComponent>(hand).Skill, Is.EqualTo(WFCrewSkill.Veteran));
            Server.System<WFCrewSetupSystem>().ApplyMission(hand, deck, new WFCrewMission { Group = "skill", Skill = WFCrewSkill.Green });
            Assert.That(SEntMan.GetComponent<WFCrewComponent>(hand).Skill, Is.EqualTo(WFCrewSkill.Green));
        });
    }
}
