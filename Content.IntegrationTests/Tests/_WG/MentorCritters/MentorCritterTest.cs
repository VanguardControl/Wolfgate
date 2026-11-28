#nullable enable
using Content.IntegrationTests.Tests._WG.MentorHelp;
using Content.IntegrationTests.Tests._WG.MentorObserve;
using Content.Server._WG.MentorCritters;
using Content.Server._WG.MentorHelp;
using Content.Server._WG.MentorObserve;
using Content.Shared._WG.MentorCritters;
using Content.Shared._WG.MentorObserve;
using Content.Shared._WG.Mentors;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Server.Nutrition.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.MentorCritters;

/// <summary>A critter joins after the asker allows it, can't be hurt, eaten or picked up, rides in a pocket and goes with the ticket.</summary>
[TestFixture]
[TestOf(typeof(WGMentorCritterSystem))]
public sealed class MentorCritterTest
{
    [Test]
    public async Task JoinRideAndGo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var visits = server.System<WGMentorVisitSystem>();
        var critters = server.System<WGMentorCritterSystem>();
        var inventory = server.System<InventorySystem>();
        var (mentor, body, asker, askerBody, ticket) = await MentorObserveTest.Setup(pair, MentorTier.Mentor);

        EntityUid critter = default;
        EntityUid stranger = default;
        await server.WaitAssertion(() =>
        {
            // A jumpsuit for the pockets, and a stranger beside the asker.
            var suit = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", entities.GetComponent<TransformComponent>(askerBody).Coordinates);
            Assert.That(inventory.TryEquip(askerBody, suit, "jumpsuit", silent: true, force: true));
            stranger = entities.SpawnEntity("MobHuman", entities.GetComponent<TransformComponent>(askerBody).Coordinates);

            var request = new WGMentorVisitRequestEvent { TicketId = ticket, Kind = MentorVisitKind.Critter, Critter = "WGMentorCritterMouse" };
            Assert.That(visits.RequestVisit(mentor, request), Is.Null);
            visits.AnswerVisit(asker, ticket, true);

            Assert.That(mentor.AttachedEntity, Is.Not.Null);
            critter = mentor.AttachedEntity!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WGMentorCritterComponent>(critter));
                Assert.That(entities.GetComponent<MetaDataComponent>(critter).EntityName, Does.Contain("mouse"));
                Assert.That(entities.HasComponent<DamageableComponent>(critter), Is.False, "Can't be hurt.");
                Assert.That(entities.HasComponent<FoodComponent>(critter), Is.False, "Isn't food.");
                Assert.That(entities.HasComponent<WGMentorAwayComponent>(body));
                Assert.That(server.System<SharedHandsSystem>().TryPickup(stranger, critter), Is.False, "Nobody picks it up.");
            });

            // Into a pocket; only the asker takes it out.
            Assert.That(critters.Ride(critter, "pocket1"));
            Assert.That(critters.IsRiding(critter));
            Assert.That(inventory.TryUnequip(stranger, askerBody, "pocket1", silent: true), Is.False, "Not the stranger's to take.");
            critters.TakeOut(critter);
            Assert.That(critters.IsRiding(critter), Is.False);

            // The ticket closes: the critter goes and the mentor is back.
            server.System<WGMentorHelpSystem>().End(asker);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.Deleted(critter) || entities.IsQueuedForDeletion(critter));
                Assert.That(mentor.AttachedEntity, Is.EqualTo(body));
                Assert.That(visits.VisitOf(mentor.UserId), Is.Null);
            });
        });

        await MentorHelpTicketTest.RestoreLoginLocal(pair);
        await pair.CleanReturnAsync();
    }
}
